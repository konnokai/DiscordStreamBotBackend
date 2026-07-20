using DiscordStreamBotBackend.Controllers;
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
                RedisDb = Redis.GetDatabase(1);
                RedisSub = Redis.GetSubscriber();
                _logger.LogInformation("Redis 已連線");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Redis 連線錯誤，請確認伺服器是否已開啟\n");
                throw;
            }

            _timer = new Timer(_ => RefreshNowRecordList(), null, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(20));
            _publisherTask = Task.Run(() => ProcessPublishQueueAsync(_shutdown.Token));
            _retryTask = Task.Run(() => ProcessPendingRetryAsync(_shutdown.Token));
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

        public void AddYouTubePubMessage(YoutubePubSubNotification youtubeNotification)
        {
            AddPubMessage(GetRedisChannelName(youtubeNotification.NotificationType), JsonConvert.SerializeObject(youtubeNotification));
        }

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

        private async Task<bool> PublishAsync(KeyValuePair<string, string> message)
        {
            try
            {
                if (await RedisSub.PublishAsync(new RedisChannel(message.Key, RedisChannel.PatternMode.Literal), message.Value) >= 1)
                    return true;

                SavePendingMessage(message);
                _logger.LogWarning("通知訊息發送失敗，儲存到清單待命 | Channel: \"{Channel}\"", message.Key);
            }
            catch (Exception ex)
            {
                SavePendingMessage(message);
                _logger.LogError(ex, "通知訊息發送錯誤 | Channel: \"{Channel}\"\n", message.Key);
            }

            return false;
        }

        private async Task RePublishPendingAsync(CancellationToken cancellationToken)
        {
            if (_needRePublishMessageList.IsEmpty)
                return;

            _logger.LogWarning("已可重新發送通知訊息");
            var pending = _needRePublishMessageList.ToArray();
            foreach (var item in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_needRePublishMessageList.TryRemove(item.Key, out var message))
                    await PublishAsync(message);
            }

            if (_needRePublishMessageList.IsEmpty)
                _logger.LogInformation("已重新發送全部通知訊息");
        }

        private void SavePendingMessage(KeyValuePair<string, string> message)
        {
            var saveKey = message.GetHashCode().ToString();
            if (message.Key.StartsWith("youtube.pubsub", StringComparison.Ordinal))
            {
                var youtubeData = JsonConvert.DeserializeObject<YoutubePubSubNotification>(message.Value);
                if (!string.IsNullOrWhiteSpace(youtubeData?.VideoId))
                    saveKey = youtubeData.VideoId;
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

                _logger.LogInformation("重整現正直播的清單: {NowRecordCount} 個直播", NowRecordList.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "現正直播清單重整失敗\n");
                NowRecordList = [];
            }
        }
    }
}
