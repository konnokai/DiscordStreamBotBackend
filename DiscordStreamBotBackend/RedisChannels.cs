namespace DiscordStreamBotBackend;

public static class RedisChannels
{
    public static class Member
    {
        public const string RevokeToken = "member.revokeToken";
    }

    public static class Twitch
    {
        public const string StreamOnline = "twitch:stream_online";
        public const string ChannelUpdate = "twitch:channel_update";
        public const string StreamOffline = "twitch:stream_offline";
        public const string AuthorizationChanged = "twitch:authorization_changed";
    }

    public static class OAuth
    {
        public static string GoogleOperationLock(ulong discordUserId)
            => $"google:oauth:operation-lock:{discordUserId}";
    }

    /// <summary>
    /// YouTube WebSub 共享狀態鍵（與 Bot 的 <c>RedisChannels.YoutubeWebSub</c> 為同一份契約）。
    /// <para>
    /// pending action 與 HMAC secret 位於 Redis logical database <see cref="DatabaseNumber"/>，
    /// 與 <see cref="Services.RedisService.RedisDb"/> 相同；不得放進 Pub/Sub payload、log 或 database 0。
    /// </para>
    /// </summary>
    public static class YoutubeWebSub
    {
        public const int DatabaseNumber = Services.RedisService.ProviderStateDatabaseIndex;

        /// <summary>每頻道一筆的未完成訂閱要求（JSON）。</summary>
        public static string PendingKey(string channelId) => $"youtube:websub:pending:{channelId}";

        /// <summary>每頻道固定的 HMAC secret（沿用既有鍵名）。</summary>
        public static string HmacSecretKey(string channelId) => $"youtube.pubsub.HMACSecret:{channelId}";
    }

    public static class AdminSettings
    {
        public const string GuildSnapshotHash = "cluster:stats:guild_snapshot";
        public const string SnapshotRequest = "cluster:admin-settings:snapshot:request";
        public const string CommandRequest = "cluster:admin-settings:command:request";

        public static string Reply(string correlationId)
            => $"cluster:admin-settings:reply:{correlationId}";
    }
}
