using Newtonsoft.Json;

namespace DiscordStreamBotBackend.Model;

public class OAuthStateData
{
    [JsonProperty("discordUserId")]
    public ulong DiscordUserId { get; set; }

    [JsonProperty("provider")]
    public string Provider { get; set; }

    [JsonProperty("frontendDomain")]
    public string FrontendDomain { get; set; }
}
