using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace DiscordStreamBotBackend.Model;

public class GoogleAccountLink
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("channelId")]
    public string ChannelId { get; set; }

    [JsonProperty("userName")]
    public string UserName { get; set; }

    [JsonProperty("profileImageUrl")]
    public string ProfileImageUrl { get; set; }

    [JsonProperty("subscriptions")]
    public IReadOnlyList<GoogleMemberSubscription> Subscriptions { get; set; } = [];

    [JsonProperty("cleanupPending")]
    public bool CleanupPending { get; set; }
}

public class GoogleMemberSubscription
{
    [JsonProperty("guildId")]
    public string GuildId { get; set; }

    [JsonProperty("channelId")]
    public string ChannelId { get; set; }

    [JsonProperty("isChecked")]
    public bool IsChecked { get; set; }

    [JsonProperty("pendingRoleRemoval")]
    public bool PendingRoleRemoval { get; set; }

    [JsonProperty("lastCheckedAt")]
    public DateTime LastCheckedAt { get; set; }
}

public class GoogleUnlinkResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = "unlinked";

    [JsonProperty("cleanupPending")]
    public bool CleanupPending { get; set; }
}

public class TwitchAccountLink
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("twitchUserId")]
    public string TwitchUserId { get; set; }

    [JsonProperty("userLogin")]
    public string UserLogin { get; set; }

    [JsonProperty("displayName")]
    public string DisplayName { get; set; }

    [JsonProperty("profileImageUrl")]
    public string ProfileImageUrl { get; set; }
}
