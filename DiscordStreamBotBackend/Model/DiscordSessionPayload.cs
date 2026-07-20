using Newtonsoft.Json;
using System;

namespace DiscordStreamBotBackend.Model;

public class DiscordSessionPayload
{
    [JsonProperty("discordUserId")]
    public ulong DiscordUserId { get; set; }

    [JsonProperty("issuedAtUtc")]
    public DateTime IssuedAtUtc { get; set; }

    [JsonProperty("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }

    [JsonProperty("purpose")]
    public string Purpose { get; set; }

    [JsonProperty("version")]
    public int Version { get; set; }
}
