using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Model.Twitch;
using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordStreamBotBackend.Tests;

public sealed class TwitchOAuthContractTests
{
    [Fact]
    public void RedisNamesMatchBotContract()
    {
        Assert.Equal("twitch:authorization_changed", RedisChannels.Twitch.AuthorizationChanged);
        Assert.Equal("twitch:oauth:refresh-lock:user-42", TwitchOAuthRefreshLock.GetKey("user-42"));
        Assert.Equal(1, RedisService.ProviderStateDatabaseIndex);
        RedisService.ValidateProviderStateDatabaseIndex(1);
    }

    [Fact]
    public void ProviderStateDatabaseRejectsDatabaseZero()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => RedisService.ValidateProviderStateDatabaseIndex(0));

        Assert.Contains("logical database 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorizationChangedPayloadMatchesBotJsonContract()
    {
        var json = JsonConvert.SerializeObject(new TwitchAuthorizationChangedPayload("user-42", "invalid"));

        Assert.Equal("{\"TwitchUserId\":\"user-42\",\"Status\":\"invalid\"}", json);
    }

    [Fact]
    public void TwitchTokenJsonAndEncryptionRoundTripMatchSharedContract()
    {
        var token = new TwitchAccessTokenData
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresIn = 3600,
            TwitchUserId = "user-42",
            Scopes = ["user:read:subscriptions"],
            TokenType = "bearer"
        };
        var json = JObject.Parse(JsonConvert.SerializeObject(token));

        Assert.Equal(
            ["access_token", "expires_in", "refresh_token", "scope", "token_type", "user_id"],
            json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));

        var service = new TokenService(CreateConfiguration());
        var encrypted = service.CreateTokenResponseToken(token);
        var roundTrip = service.GetTokenResponseValue<TwitchAccessTokenData>(encrypted);

        Assert.NotNull(roundTrip);
        Assert.Equal(token.AccessToken, roundTrip.AccessToken);
        Assert.Equal(token.RefreshToken, roundTrip.RefreshToken);
        Assert.Equal(token.TwitchUserId, roundTrip.TwitchUserId);
        Assert.Equal(token.Scopes, roundTrip.Scopes);
    }

    [Fact]
    public void StartupValidationRejectsMissingProviderTokenEncryptionKey()
    {
        var values = CreateValidConfigurationValues();
        values["Token:ProviderTokenEncryptionKey"] = "short";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => StartupValidationHostedService.ValidateConfiguration(configuration));

        Assert.Contains("Token:ProviderTokenEncryptionKey", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingProviderTokenCanFinalizeUnlink(string? encryptedAccessToken)
    {
        Assert.True(TwitchAuthorizationService.CanFinalizeRevocationWithoutProviderToken(encryptedAccessToken!));
    }

    [Fact]
    public void TokenNormalizationEncryptsBotUsableRoundTrip()
    {
        var token = new TwitchAccessTokenData
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresIn = 1,
            TokenType = " BEARER "
        };
        var validation = new TwitchValidateTokenData
        {
            ClientId = "twitch-client",
            UserId = "user-42",
            Scopes = ["user:read:subscriptions", "user:read:subscriptions"],
            ExpiresIn = 3600
        };

        var normalized = TwitchAuthorizationService.NormalizeTokenForPersistence(
            token,
            validation,
            token.RefreshToken,
            token.TokenType);
        var tokenService = new TokenService(CreateConfiguration());
        var encrypted = tokenService.CreateTokenResponseToken(normalized);
        var roundTrip = tokenService.GetTokenResponseValue<TwitchAccessTokenData>(encrypted);

        Assert.True(TwitchAuthorizationService.IsUsableTokenForBot(roundTrip, "user-42"));
        Assert.Equal("user-42", roundTrip.TwitchUserId);
        Assert.Equal("bearer", roundTrip.TokenType);
        Assert.Equal(3600, roundTrip.ExpiresIn);
        Assert.Equal(["user:read:subscriptions"], roundTrip.Scopes);
    }

    [Theory]
    [InlineData("invalid", true)]
    [InlineData("revoked", true)]
    [InlineData("unlinked", true)]
    [InlineData("linked", false)]
    [InlineData("refreshed", false)]
    public void OnlyInvalidationStatusesCanTriggerRoleCleanup(string status, bool expected)
    {
        Assert.Equal(expected, TwitchAuthorizationService.IsInvalidationStatus(status));
    }

    [Fact]
    public void InvalidReplayPolicyRejectsRelinkedCurrentRow()
    {
        var invalidatedAt = new DateTime(2026, 8, 4, 1, 2, 3, DateTimeKind.Utc);
        var staleInvalid = AuthorizationState(invalidatedAt, "refresh_invalid", invalidatedAt);
        var relinked = AuthorizationState(invalidatedAt.AddSeconds(1), null, null);

        Assert.False(TwitchAuthorizationService.ShouldPublishAuthorizationChange(staleInvalid, relinked, "invalid"));
        Assert.True(TwitchAuthorizationService.ShouldPublishAuthorizationChange(
            staleInvalid,
            AuthorizationState(invalidatedAt, "refresh_invalid", invalidatedAt),
            "invalid"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("revocation_pending", true)]
    [InlineData("user_unlinked", false)]
    [InlineData("refresh_invalid", false)]
    public void RotatedTokenRetryRecognizesOnlyDurableNonFinalStates(string? revocationReason, bool expected)
    {
        Assert.Equal(
            expected,
            TwitchAuthorizationService.IsRefreshedTokenPersistenceSatisfied(
                "new-token",
                revocationReason!,
                "new-token"));
        Assert.False(TwitchAuthorizationService.IsRefreshedTokenPersistenceSatisfied(
            "old-token",
            revocationReason!,
            "new-token"));
    }

    private static TwitchBroadcasterAuthorization AuthorizationState(
        DateTime dateUpdated,
        string? revocationReason,
        DateTime? revokedAt)
        => new()
        {
            TwitchUserId = "user-42",
            DateUpdated = dateUpdated,
            RevocationReason = revocationReason!,
            RevokedAt = revokedAt
        };

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder().AddInMemoryCollection(CreateValidConfigurationValues()).Build();

    private static Dictionary<string, string?> CreateValidConfigurationValues()
        => new()
        {
            ["Token:Frontend"] = new string('f', 64),
            ["Token:ProviderTokenEncryptionKey"] = new string('p', 64),
            ["FrontendDomain"] = "https://frontend.test",
            ["ApiServerDomain"] = "https://api.test",
            ["Discord:ClientId"] = "discord-client",
            ["Discord:ClientSecret"] = "discord-secret",
            ["Google:ClientId"] = "google-client",
            ["Google:ClientSecret"] = "google-secret",
            ["Twitch:ClientId"] = "twitch-client",
            ["Twitch:ClientSecret"] = "twitch-secret",
            ["ConnectionStrings:MySql"] = "Server=mysql;Database=test;User=test;Password=test",
            ["ConnectionStrings:Redis"] = "redis:6379"
        };
}

public sealed class TwitchRefreshRotationLifecycleTests
{
    [Fact]
    public async Task StopRejectsNewRefreshAndWaitsForInFlightHandoff()
    {
        var lifecycle = new TwitchRefreshRotationLifecycle();
        Assert.True(lifecycle.TryBeginRefresh(out var refresh));

        var stop = lifecycle.StopAcceptingAndDrainAsync();

        Assert.False(lifecycle.TryBeginRefresh(out _));
        Assert.False(stop.IsCompleted);
        refresh.Dispose();
        await stop.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AcceptedRotationRemainsTrackedUntilPersistenceCompletes()
    {
        var pendingCounts = new List<int>();
        var lifecycle = new TwitchRefreshRotationLifecycle(pendingCounts.Add);
        Assert.True(lifecycle.TryBeginRefresh(out var refresh));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifecycle.TrackAcceptedPersistence(persisted.Task);

        var stop = lifecycle.StopAcceptingAndDrainAsync();
        refresh.Dispose();

        Assert.False(stop.IsCompleted);
        Assert.Equal(1, lifecycle.PendingPersistenceCount);
        persisted.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, lifecycle.PendingPersistenceCount);
        Assert.Equal([1, 0], pendingCounts);
    }

    [Fact]
    public async Task InFlightRefreshCanHandAcceptedRotationToDrainAfterStopStarts()
    {
        var lifecycle = new TwitchRefreshRotationLifecycle();
        Assert.True(lifecycle.TryBeginRefresh(out var refresh));
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var stop = lifecycle.StopAcceptingAndDrainAsync();
        lifecycle.TrackAcceptedPersistence(persisted.Task);
        refresh.Dispose();

        Assert.False(stop.IsCompleted);
        persisted.SetResult();
        await stop.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, lifecycle.ActiveOperationCount);
        Assert.Equal(0, lifecycle.PendingPersistenceCount);
    }

    [Fact]
    public async Task CompletedOrStalePersistenceDoesNotLeakTaskTracking()
    {
        var lifecycle = new TwitchRefreshRotationLifecycle();
        Assert.True(lifecycle.TryBeginRefresh(out var refresh));
        lifecycle.TrackAcceptedPersistence(Task.CompletedTask);
        refresh.Dispose();

        await lifecycle.StopAcceptingAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, lifecycle.ActiveOperationCount);
        Assert.Equal(0, lifecycle.PendingPersistenceCount);
    }

    [Fact]
    public async Task MetricCallbackFailureCannotBreakDrain()
    {
        var lifecycle = new TwitchRefreshRotationLifecycle(
            _ => throw new InvalidOperationException("metric failure"));
        Assert.True(lifecycle.TryBeginRefresh(out var refresh));
        lifecycle.TrackAcceptedPersistence(Task.CompletedTask);
        refresh.Dispose();

        await lifecycle.StopAcceptingAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, lifecycle.PendingPersistenceCount);
    }
}
