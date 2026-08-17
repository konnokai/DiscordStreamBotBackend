using Newtonsoft.Json;

namespace DiscordStreamBotBackend.Model;

public class DiscordAccessTokenData
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
}
