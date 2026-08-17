using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace DiscordStreamBotBackend.Model;

public class AdminGuild
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("icon")]
    public string Icon { get; set; }

    [JsonProperty("owner")]
    public bool Owner { get; set; }

    [JsonProperty("permissions")]
    public string Permissions { get; set; }

    [JsonProperty("botInstalled")]
    public bool BotInstalled { get; set; }
}

public class AdminSettingsCommandRequest
{
    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("payload")]
    public JObject Payload { get; set; }
}

public class AdminSettingsRequestEnvelope
{
    [JsonProperty("contractVersion")]
    public int ContractVersion { get; set; } = 1;

    [JsonProperty("correlationId")]
    public string CorrelationId { get; set; }

    [JsonProperty("guildId")]
    public string GuildId { get; set; }

    [JsonProperty("actorUserId")]
    public string ActorUserId { get; set; }

    [JsonProperty("action")]
    public string Action { get; set; }

    [JsonProperty("payload")]
    public JObject Payload { get; set; }
}

public class AdminSettingsCommandReply
{
    [JsonProperty("contractVersion")]
    public int ContractVersion { get; set; }

    [JsonProperty("correlationId")]
    public string CorrelationId { get; set; }

    [JsonProperty("shardId", NullValueHandling = NullValueHandling.Ignore)]
    public int? ShardId { get; set; }

    [JsonProperty("state")]
    public string State { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("arguments")]
    public JObject Arguments { get; set; }
}

public class AdminSettingsSnapshotReply
{
    [JsonProperty("contractVersion")]
    public int ContractVersion { get; set; }

    [JsonProperty("capabilities")]
    public List<string> Capabilities { get; set; }

    [JsonProperty("guild")]
    public JObject Guild { get; set; }

    [JsonProperty("health")]
    public JObject Health { get; set; }

    [JsonProperty("resources")]
    public JObject Resources { get; set; }

    [JsonProperty("common")]
    public JObject Common { get; set; }

    [JsonProperty("notifications")]
    public JObject Notifications { get; set; }

    [JsonProperty("crawlers")]
    public JObject Crawlers { get; set; }

    [JsonProperty("verification")]
    public JObject Verification { get; set; }
}

internal class BotGuildSnapshot
{
    [JsonProperty("id")]
    public string Id { get; set; }
}

internal class BotGuildSnapshotEnvelope
{
    [JsonProperty("guilds")]
    public List<BotGuildSnapshot> Guilds { get; set; }
}
