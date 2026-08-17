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

    public static class AdminSettings
    {
        public const string GuildSnapshotHash = "cluster:stats:guild_snapshot";
        public const string SnapshotRequest = "cluster:admin-settings:snapshot:request";
        public const string CommandRequest = "cluster:admin-settings:command:request";

        public static string Reply(string correlationId)
            => $"cluster:admin-settings:reply:{correlationId}";
    }
}
