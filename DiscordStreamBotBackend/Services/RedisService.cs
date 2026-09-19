using DiscordStreamBotBackend.YoutubeWebSub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services
{
    public class RedisService : IDisposable
    {
        public const int ProviderStateDatabaseIndex = 1;

        public List<string> NowRecordList { get; private set; } = new List<string>();
        public ConnectionMultiplexer Redis { get; set; }
        public ISubscriber RedisSub { get; set; }
        public IDatabase RedisDb { get; set; }

        private readonly ILogger<RedisService> _logger;
        private readonly Channel<KeyValuePair<string, string>> _messageQueue = Channel.CreateBounded<KeyValuePair<string, string>>(
            new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        private readonly ConcurrentDictionary<string, KeyValuePair<string, string>> _needRePublishMessageList = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Timer _timer;
        private readonly Task _publisherTask;
        private readonly Task _retryTask;

        public RedisService(ILogger<RedisService> logger, IConfiguration configuration)
        {
            _logger = logger;

            try
            {
                RedisConnection.Init(configuration.GetConnectionString("Redis"));
                Redis = RedisConnection.Instance.ConnectionMultiplexer;
                RedisDb = Redis.GetDatabase(ProviderStateDatabaseIndex);
                ValidateProviderStateDatabaseIndex(RedisDb.Database);
                RedisSub = Redis.GetSubscriber();
                _logger.LogInformation("Redis 連線成功。");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Redis 連線失敗，請確認 Redis 服務是否已啟動。");
                throw;
            }

            _timer = new Timer(_ => RefreshNowRecordList(), null, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(20));
            _publisherTask = Task.Run(() => ProcessPublishQueueAsync(_shutdown.Token));
            _retryTask = Task.Run(() => ProcessPendingRetryAsync(_shutdown.Token));
        }

        internal static void ValidateProviderStateDatabaseIndex(int databaseIndex)
        {
            if (databaseIndex != ProviderStateDatabaseIndex)
            {
                throw new InvalidOperationException(
                    $"Twitch OAuth 共用狀態必須使用 Redis 邏輯資料庫 {ProviderStateDatabaseIndex}，目前使用的是資料庫 {databaseIndex}。");
            }
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _messageQueue.Writer.TryComplete();
            _timer.Change(Timeout.Infinite, 0);
            _timer.Dispose();
            try { Task.WhenAll(_publisherTask, _retryTask).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }

            RedisSub.UnsubscribeAll();
            Redis.Dispose();
            _shutdown.Dispose();
        }

        public void AddPubMessage(string channel, string msg)
        {
            AddPubMessageAsync(channel, msg).AsTask().GetAwaiter().GetResult();
        }

        public ValueTask AddPubMessageAsync(string channel, string msg, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_messageQueue.Writer.TryWrite(new KeyValuePair<string, string>(channel, msg)))
                return ValueTask.CompletedTask;

            if (channel == RedisChannels.Twitch.StreamOnline || channel == RedisChannels.Twitch.ChannelUpdate || channel == RedisChannels.Twitch.StreamOffline)
                BackendMetrics.TwitchWebhookQueueDropped.Inc();
            throw new InvalidOperationException("Redis 發布佇列已滿或正在關閉。");
        }

        /// <summary>轉發 WebSub 通知到既有 Redis channel；實際發布與重試由既有佇列處理，不讓 Hub request 等待。</summary>
        public ValueTask AddYouTubePubMessageAsync(YoutubePubSubNotification youtubeNotification)
            => AddPubMessageAsync(GetRedisChannelName(youtubeNotification.NotificationType), JsonConvert.SerializeObject(youtubeNotification));

        private static string GetRedisChannelName(YoutubePubSubNotification.YTNotificationType notificationType)
            => notificationType == YoutubePubSubNotification.YTNotificationType.CreateOrUpdated ? "youtube.pubsub.CreateOrUpdate" : "youtube.pubsub.Deleted";

        private async Task ProcessPublishQueueAsync(CancellationToken cancellationToken)
        {
            await foreach (var message in _messageQueue.Reader.ReadAllAsync(cancellationToken))
                await PublishAsync(message);
        }

        private async Task ProcessPendingRetryAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RePublishPendingAsync(cancellationToken);
        }

        private async Task<bool> PublishAsync(KeyValuePair<string, string> message, bool isLogWarning = true)
        {
            try
            {
                if (await RedisSub.PublishAsync(new RedisChannel(message.Key, RedisChannel.PatternMode.Literal), message.Value) >= 1)
                    return true;

                SavePendingMessage(message);
                if (isLogWarning)
                    _logger.LogWarning("通知訊息傳送失敗，已加入待重試清單 | Channel: \"{Channel}\"", message.Key);
            }
            catch (Exception ex)
            {
                SavePendingMessage(message);
                _logger.LogError(ex, "傳送通知訊息時發生錯誤 | Channel: \"{Channel}\"", message.Key);
            }

            if (message.Key == RedisChannels.Member.RevokeToken)
                BackendMetrics.GoogleCleanupWakeupPublishFailures.Inc();

            return false;
        }

        private async Task RePublishPendingAsync(CancellationToken cancellationToken)
        {
            if (_needRePublishMessageList.IsEmpty)
                return;

            _logger.LogWarning("重新傳送通知訊息：{Count} 筆", _needRePublishMessageList.Count);
            var pending = _needRePublishMessageList.ToArray();
            foreach (var item in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_needRePublishMessageList.TryRemove(item.Key, out var message))
                    await PublishAsync(message, false);
            }

            if (_needRePublishMessageList.IsEmpty)
                _logger.LogInformation("通知訊息已全部重新傳送。");
        }

        /// <summary>
        /// 待重試訊息的識別 key。WebSub 通知（CreateOrUpdate／Deleted）以 videoId 去重；
        /// 其餘訊息（例如 <c>youtube.pubsub.NeedRegister</c> 的裸 channel ID）用 channel + payload 的完整字串，
        /// 不可用雜湊值，否則不同頻道的訊息會互相覆蓋。
        /// </summary>
        private void SavePendingMessage(KeyValuePair<string, string> message)
        {
            var saveKey = $"{message.Key}|{message.Value}";
            if (message.Key is "youtube.pubsub.CreateOrUpdate" or "youtube.pubsub.Deleted")
            {
                try
                {
                    var youtubeData = JsonConvert.DeserializeObject<YoutubePubSubNotification>(message.Value);
                    if (!string.IsNullOrWhiteSpace(youtubeData?.VideoId))
                        saveKey = youtubeData.VideoId;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "待重試的 YouTube 通知無法解析，改用訊息內容作為識別 | Channel: \"{Channel}\"", message.Key);
                }
            }

            _needRePublishMessageList.AddOrUpdate(saveKey, message, (_, _) => message);
        }

        private void RefreshNowRecordList()
        {
            try
            {
                var newNowRecordList = Redis.GetDatabase(0).SetMembers("youtube.nowRecord").Select(x => x.ToString()).ToList();
                if (newNowRecordList.Any())
                {
                    NowRecordList.Clear();
                    NowRecordList.AddRange(newNowRecordList);
                }

                _logger.LogInformation("更新目前直播清單：{NowRecordCount} 個直播", NowRecordList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新目前直播清單失敗。");
                NowRecordList = [];
            }
        }
    }
}
