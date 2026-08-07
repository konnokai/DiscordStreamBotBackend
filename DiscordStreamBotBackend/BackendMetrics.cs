using Prometheus;

namespace DiscordStreamBotBackend;

public static class BackendMetrics
{
    public static readonly Counter OAuthAttempts = Metrics.CreateCounter(
        "discord_stream_notify_oauth_attempts_total",
        "OAuth 流程嘗試次數。",
        new CounterConfiguration { LabelNames = ["provider", "result"] });

    public static readonly Gauge OAuthLinkedAccounts = Metrics.CreateGauge(
        "discord_stream_notify_oauth_linked_accounts",
        "OAuth 連結帳號數。",
        new GaugeConfiguration { LabelNames = ["provider", "status"] });

    public static readonly Counter OAuthTokenValidations = Metrics.CreateCounter(
        "discord_stream_notify_oauth_token_validations_total",
        "OAuth token 驗證次數。",
        new CounterConfiguration { LabelNames = ["provider", "result"] });

    public static readonly Counter OAuthTokenRefreshes = Metrics.CreateCounter(
        "discord_stream_notify_oauth_token_refreshes_total",
        "OAuth token 更新次數。",
        new CounterConfiguration { LabelNames = ["provider", "result"] });

    public static readonly Counter GoogleCleanupWakeupPublishFailures = Metrics.CreateCounter(
        "discord_stream_notify_google_cleanup_wakeup_publish_failures_total",
        "Google 解除連結已提交後，Redis 角色清理喚醒通知失敗次數。");

    public static readonly Gauge TwitchRefreshPendingPersistence = Metrics.CreateGauge(
        "discord_stream_notify_twitch_refresh_pending_persistence",
        "尚未完成 MySQL 保存的 Twitch refresh token rotation 數量。");

    public static readonly Gauge TwitchRefreshShutdownDraining = Metrics.CreateGauge(
        "discord_stream_notify_twitch_refresh_shutdown_draining",
        "Backend 是否正在等待已接受的 Twitch refresh token rotation 保存完成。");

    public static readonly Histogram TwitchRefreshShutdownDrainDuration = Metrics.CreateHistogram(
        "discord_stream_notify_twitch_refresh_shutdown_drain_duration_seconds",
        "Backend 關閉時等待 Twitch refresh token rotation 保存的耗時秒數。");

    public static readonly Counter TwitchWebhookEvents = Metrics.CreateCounter(
        "discord_stream_notify_twitch_webhook_events_total",
        "Twitch Webhook 事件數。",
        new CounterConfiguration { LabelNames = ["type", "result"] });

    public static readonly Counter TwitchWebhookQueueDropped = Metrics.CreateCounter(
        "discord_stream_notify_twitch_webhook_queue_dropped_total",
        "Twitch Webhook 佇列無法收件次數。");

    public static readonly Gauge TwitchWebhookLastReceived = Metrics.CreateGauge(
        "discord_stream_notify_twitch_webhook_last_received_unixtime",
        "最後收到 Twitch Webhook 的 Unix 時間。",
        new GaugeConfiguration { LabelNames = ["type"] });
}
