using DiscordStreamBotBackend.Controllers;
using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;

namespace DiscordStreamBotBackend.Tests;

public sealed class AdminSettingsContractTests
{
    private const ulong DiscordUserId = 18446744073709551615UL;

    [Fact]
    public void DiscordSessionKeepsAccessTokenAndUsesProviderExpiryWhenEarlier()
    {
        var tokenService = new TokenService(CreateConfiguration());
        var bearerTokenService = new BearerTokenService(tokenService);
        var before = DateTime.UtcNow;

        var token = bearerTokenService.CreateDiscordSessionToken(DiscordUserId, "discord-access", 3600);
        var after = DateTime.UtcNow;
        var payload = tokenService.GetUser<DiscordSessionPayload>(token);

        Assert.NotNull(payload);
        Assert.Equal("discord-access", payload.DiscordAccessToken);
        Assert.InRange(payload.ProviderExpiresAtUtc, before.AddHours(1), after.AddHours(1));
        Assert.Equal(payload.ProviderExpiresAtUtc, payload.ExpiresAtUtc);
        Assert.True(bearerTokenService.TryGetDiscordSession($"Bearer {token}", out var session));
        Assert.Equal(DiscordUserId, session.DiscordUserId);
        Assert.DoesNotContain("discord-access", token, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscordSessionLifetimeIsCappedAtTwelveHours()
    {
        var tokenService = new TokenService(CreateConfiguration());
        var bearerTokenService = new BearerTokenService(tokenService);
        var before = DateTime.UtcNow;

        var token = bearerTokenService.CreateDiscordSessionToken(DiscordUserId, "discord-access", 86400);
        var after = DateTime.UtcNow;
        var payload = tokenService.GetUser<DiscordSessionPayload>(token);

        Assert.InRange(payload.ExpiresAtUtc, before.AddHours(12), after.AddHours(12));
        Assert.InRange(payload.ProviderExpiresAtUtc, before.AddDays(1), after.AddDays(1));
    }

    [Fact]
    public void LegacySessionStillSupportsExistingAccountLinkIdentityOnlyAuth()
    {
        var tokenService = new TokenService(CreateConfiguration());
        var bearerTokenService = new BearerTokenService(tokenService);
        var now = DateTime.UtcNow;
        var token = tokenService.CreateToken(new DiscordSessionPayload
        {
            DiscordUserId = DiscordUserId,
            IssuedAtUtc = now.AddMinutes(-10),
            ExpiresAtUtc = now.AddHours(1),
            Purpose = "discord_session",
            Version = 1
        });

        Assert.True(bearerTokenService.TryGetDiscordUserIdFromToken(token, out var discordUserId));
        Assert.Equal(DiscordUserId, discordUserId);
        Assert.False(bearerTokenService.TryGetDiscordSession($"Bearer {token}", out _));
    }

    [Theory]
    [InlineData(true, "0", true)]
    [InlineData(false, "8", true)]
    [InlineData(false, "40", true)]
    [InlineData(false, "32", false)]
    [InlineData(false, "not-a-number", false)]
    public void OnlyOwnerOrAdministratorCanManage(bool owner, string permissions, bool expected)
    {
        Assert.Equal(expected, DiscordGuildAuthorizationService.CanManage(new AdminGuild
        {
            Owner = owner,
            Permissions = permissions
        }));
    }

    [Fact]
    public async Task GuildListCacheIsPerUserAndReturnsIndependentResults()
    {
        var handler = new GuildListHandler();
        var service = new DiscordGuildAuthorizationService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));
        var issuedAt = DateTime.UtcNow;
        var expiresAt = DateTime.UtcNow.AddMinutes(1);

        var first = await service.GetManageableGuildsAsync(1, "token", issuedAt, expiresAt, true, default);
        first[0].BotInstalled = true;
        var cached = await service.GetManageableGuildsAsync(1, "token", issuedAt, expiresAt, true, default);
        await service.GetManageableGuildsAsync(2, "token", issuedAt, expiresAt, true, default);

        Assert.Equal(2, handler.RequestCount);
        Assert.False(cached[0].BotInstalled);
    }

    [Fact]
    public void AdminEnvelopeUsesCamelCaseAndSnowflakeStrings()
    {
        var envelope = AdminGuildsController.CreateEnvelope(
            DiscordUserId.ToString(),
            DiscordUserId,
            "youtube-notification.remove",
            JObject.FromObject(new { sourceId = "UC123" }),
            1780000000000);
        var json = JObject.Parse(JsonConvert.SerializeObject(envelope));

        Assert.Equal(1, json.Value<int>("contractVersion"));
        Assert.Equal(32, json.Value<string>("correlationId")!.Length);
        Assert.Equal(JTokenType.String, json["guildId"]!.Type);
        Assert.Equal(DiscordUserId.ToString(), json.Value<string>("guildId"));
        Assert.Equal(JTokenType.String, json["actorUserId"]!.Type);
        Assert.Equal(DiscordUserId.ToString(), json.Value<string>("actorUserId"));
        Assert.Equal(1780000000000, json.Value<long>("deadlineUnixMs"));
        Assert.Equal("youtube-notification.remove", json.Value<string>("action"));
        Assert.Equal("UC123", json["payload"]!.Value<string>("sourceId"));
        Assert.Equal(
            ["action", "actorUserId", "contractVersion", "correlationId", "deadlineUnixMs", "guildId", "payload"],
            json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void RedisTransportOutcomesDoNotCollapseIntoOneState()
    {
        Assert.Equal(
            AdminSettingsRedisOutcome.Reply,
            AdminSettingsRedisResult<string>.FromReply("reply").Outcome);
        Assert.Equal(
            AdminSettingsRedisOutcome.Unavailable,
            AdminSettingsRedisResult<string>.Unavailable().Outcome);
        Assert.Equal(
            AdminSettingsRedisOutcome.DeadlineExceeded,
            AdminSettingsRedisResult<string>.DeadlineExceeded().Outcome);
    }

    [Fact]
    public void RedisChannelsMatchAdminSettingsContract()
    {
        Assert.Equal("cluster:stats:guild_snapshot", RedisChannels.AdminSettings.GuildSnapshotHash);
        Assert.Equal("cluster:admin-settings:snapshot:request", RedisChannels.AdminSettings.SnapshotRequest);
        Assert.Equal("cluster:admin-settings:command:request", RedisChannels.AdminSettings.CommandRequest);
        Assert.Equal(
            "cluster:admin-settings:reply:abc123",
            RedisChannels.AdminSettings.Reply("abc123"));
    }

    [Fact]
    public void ExpandedSnapshotPassesCrawlerAndVerificationDataThrough()
    {
        var snapshot = JsonConvert.DeserializeObject<AdminSettingsSnapshotReply>("""
            {"contractVersion":1,"capabilities":[],"guild":{},"health":{},"resources":{},"common":{},
             "notifications":{},"crawlers":{"youtube":{"count":1}},"verification":{"twitch":[]}}
            """);

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.Crawlers["youtube"]!["count"]!.Value<int>());
        Assert.Equal(JTokenType.Array, snapshot.Verification["twitch"]!.Type);
    }

    [Fact]
    public void MissingAdminSettingsRepliesAreRejected()
    {
        Assert.False(AdminGuildsController.TryReadSnapshotReply(null!, out _));
        Assert.False(AdminGuildsController.TryReadCommandReply(null!, "correlation", out _));
    }

    [Fact]
    public void RedisGuildSnapshotParserAcceptsLegacyArrayAndEnvelope()
    {
        var legacy = AdminSettingsRedisService.ParseGuildSnapshot(
            "[{\"Id\":18446744073709551615,\"Name\":\"legacy\"}]");
        var envelope = AdminSettingsRedisService.ParseGuildSnapshot(
            "{\"ShardId\":1,\"Guilds\":[{\"Id\":\"9007199254740993\",\"Name\":\"current\"}]}");

        Assert.Equal(["18446744073709551615"], legacy);
        Assert.Equal(["9007199254740993"], envelope);
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Token:Frontend"] = new string('f', 64),
            ["Token:ProviderTokenEncryptionKey"] = new string('p', 64)
        }).Build();

    private sealed class GuildListHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\":\"1\",\"name\":\"Guild\",\"owner\":true,\"permissions\":\"0\"}]")
            });
        }
    }
}
