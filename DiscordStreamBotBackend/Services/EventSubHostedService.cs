using System;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Core.EventArgs.Stream;
using TwitchLib.EventSub.Webhooks.Core;
using TwitchLib.EventSub.Webhooks.Core.EventArgs;

namespace DiscordStreamBotBackend.Services
{
    // https://github.com/TwitchLib/TwitchLib.EventSub.Webhooks/blob/main/TwitchLib.EventSub.Webhooks.Example/EventSubHostedService.cs
    public class EventSubHostedService : IHostedService
    {
        private readonly ILogger<EventSubHostedService> _logger;
        private readonly IEventSubWebhooks _eventSubWebhooks;
        private readonly RedisService _redisService;

        public EventSubHostedService(ILogger<EventSubHostedService> logger, IEventSubWebhooks eventSubWebhooks, RedisService redisService)
        {
            _logger = logger;
            _eventSubWebhooks = eventSubWebhooks;
            _redisService = redisService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _eventSubWebhooks.Error += OnError;
            _eventSubWebhooks.StreamOnline += OnStreamOnline;
            _eventSubWebhooks.StreamOffline += _eventSubWebhooks_OnStreamOffline;
            _eventSubWebhooks.ChannelUpdate += _eventSubWebhooks_OnChannelUpdate;
            _eventSubWebhooks.Revocation += (_, _) =>
            {
                BackendMetrics.TwitchWebhookEvents.WithLabels("revocation", "received").Inc();
                BackendMetrics.TwitchWebhookLastReceived.WithLabels("revocation").Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _logger.LogWarning("收到 Twitch EventSub 訂閱撤銷事件。");
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _eventSubWebhooks.Error -= OnError;
            _eventSubWebhooks.StreamOnline -= OnStreamOnline;
            _eventSubWebhooks.StreamOffline -= _eventSubWebhooks_OnStreamOffline;
            _eventSubWebhooks.ChannelUpdate -= _eventSubWebhooks_OnChannelUpdate;
            return Task.CompletedTask;
        }

        private Task OnError(object sender, OnErrorArgs e)
        {
            BackendMetrics.TwitchWebhookEvents.WithLabels("unknown", "error").Inc();
            _logger.LogError("Twitch 發生錯誤，原因：{Reason}，訊息：{Message}", e.Reason, e.Message);
            return Task.CompletedTask;
        }

        private Task OnStreamOnline(object sender, StreamOnlineArgs e)
        {
            _logger.LogInformation("Twitch 直播已開始: {UserName} ({UserId})", e.Payload.Event.BroadcasterUserName, e.Payload.Event.BroadcasterUserId);
            return PublishAsync("stream_online", RedisChannels.Twitch.StreamOnline, e.Payload.Event);
        }

        private Task _eventSubWebhooks_OnStreamOffline(object sender, StreamOfflineArgs e)
        {
            _logger.LogInformation("Twitch 直播已離線: {UserName} ({UserId})", e.Payload.Event.BroadcasterUserName, e.Payload.Event.BroadcasterUserId);
            return PublishAsync("stream_offline", RedisChannels.Twitch.StreamOffline, e.Payload.Event);
        }

        private Task _eventSubWebhooks_OnChannelUpdate(object sender, ChannelUpdateArgs e)
        {
            _logger.LogInformation("Twitch 頻道狀態更新：{UserName} - {Title}（{CategoryName}）",
                e.Payload.Event.BroadcasterUserName,
                e.Payload.Event.Title,
                e.Payload.Event.CategoryName);

            return PublishAsync("channel_update", RedisChannels.Twitch.ChannelUpdate, e.Payload.Event);
        }

        private async Task PublishAsync(string type, string channel, object payload)
        {
            BackendMetrics.TwitchWebhookEvents.WithLabels(type, "received").Inc();
            BackendMetrics.TwitchWebhookLastReceived.WithLabels(type).Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            try
            {
                await _redisService.AddPubMessageAsync(channel, JsonConvert.SerializeObject(payload));
                BackendMetrics.TwitchWebhookEvents.WithLabels(type, "enqueued").Inc();
            }
            catch (Exception ex)
            {
                BackendMetrics.TwitchWebhookEvents.WithLabels(type, "error").Inc();
                _logger.LogError(ex, "Twitch Webhook 事件無法加入 Redis 發布佇列，類型：{Type}", type);
                throw;
            }
        }
    }
}
