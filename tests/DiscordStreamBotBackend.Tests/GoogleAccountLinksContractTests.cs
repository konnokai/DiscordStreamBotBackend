using DiscordStreamBotBackend.Controllers;
using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiscordStreamBotBackend.Tests;

public sealed class GoogleAccountLinksContractTests
{
    [Fact]
    public void GoogleAccountLinkSerializesPendingCleanupContractWithLowerCamelNames()
    {
        var account = new GoogleAccountLink
        {
            Status = "unlinked",
            Subscriptions = []
        };
        var json = JObject.Parse(JsonConvert.SerializeObject(account));

        Assert.NotNull(json.Property("cleanupPending"));
        Assert.False(json.Value<bool>("cleanupPending"));
        Assert.NotNull(typeof(GoogleAccountLink).GetProperty("CleanupPending"));
    }

    [Fact]
    public void GoogleMemberSubscriptionSerializesPendingRoleRemovalContract()
    {
        var subscription = new GoogleMemberSubscription
        {
            GuildId = ulong.MaxValue.ToString(),
            ChannelId = "UC123",
            IsChecked = false,
            PendingRoleRemoval = true,
            LastCheckedAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        };
        var json = JObject.Parse(JsonConvert.SerializeObject(subscription));

        Assert.NotNull(json.Property("pendingRoleRemoval"));
        Assert.True(json.Value<bool>("pendingRoleRemoval"));
        Assert.NotNull(typeof(GoogleMemberSubscription).GetProperty("PendingRoleRemoval"));
        Assert.Equal(JTokenType.String, json["guildId"]?.Type);
        Assert.Equal("18446744073709551615", json.Value<string>("guildId"));
        Assert.Equal(
            ["channelId", "guildId", "isChecked", "lastCheckedAt", "pendingRoleRemoval"],
            json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void GoogleUnlinkResponseUsesLowerCamelContract()
    {
        var json = JObject.Parse(JsonConvert.SerializeObject(new GoogleUnlinkResponse
        {
            CleanupPending = true
        }));

        Assert.Equal("unlinked", json.Value<string>("status"));
        Assert.True(json.Value<bool>("cleanupPending"));
        Assert.Equal(
            ["cleanupPending", "status"],
            json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void MemberCleanupWakeupMatchesBotRedisContract()
    {
        Assert.Equal("member.revokeToken", RedisChannels.Member.RevokeToken);
        Assert.Equal(
            "18446744073709551615",
            GoogleMemberCleanupWakeupPublisher.GetPayload(ulong.MaxValue));
    }

    [Fact]
    public void YoutubeMemberCheckModelMirrorsBotOwnedIndexes()
    {
        var options = new DbContextOptionsBuilder<MainDbContext>()
            .UseMySql(
                "Server=localhost;Database=model_only;User=model;Password=model",
                new MySqlServerVersion(new Version(8, 0, 0)))
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new MainDbContext(options);
        var entity = db.Model.FindEntityType(typeof(YoutubeMemberCheck));
        Assert.NotNull(entity);

        Assert.Equal("longtext", entity!.FindProperty(nameof(YoutubeMemberCheck.CheckYtChannelId))?.GetColumnType());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique && PropertyNames(index) == "GuildId,UserId,CheckYtChannelId");
        Assert.Contains(entity.GetIndexes(), index => PropertyNames(index) == "PendingRoleRemoval,GuildId");
        Assert.Contains(entity.GetIndexes(), index => PropertyNames(index) == "UserId,PendingRoleRemoval");

        var tokenEntity = db.Model.FindEntityType(typeof(YoutubeMemberAccessToken));
        Assert.NotNull(tokenEntity);
        Assert.Equal("youtube_member_access_token", tokenEntity!.GetTableName());
        var tokenTable = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
            tokenEntity.GetTableName()!,
            tokenEntity.GetSchema());
        Assert.Equal(
            "discord_user_id",
            tokenEntity.FindProperty(nameof(YoutubeMemberAccessToken.DiscordUserId))?.GetColumnName(tokenTable));
        Assert.Equal(
            "encrypted_access_token",
            tokenEntity.FindProperty(nameof(YoutubeMemberAccessToken.EncryptedAccessToken))?.GetColumnName(tokenTable));

        var unlinkIntentEntity = db.Model.FindEntityType(typeof(GoogleOAuthUnlinkIntent));
        Assert.NotNull(unlinkIntentEntity);
        Assert.Equal("google_oauth_unlink_intent", unlinkIntentEntity!.GetTableName());
    }

    [Fact]
    public async Task GoogleSdkDataStoreCannotMutateAuthoritativeTokenState()
    {
        var store = new NonPersistentGoogleDataStore();

        await store.StoreAsync("42", new { AccessToken = "ignored" });
        await store.DeleteAsync<object>("42");

        Assert.Null(await store.GetAsync<object>("42"));
    }

    [Theory]
    [InlineData(400, "{\"error\":\"invalid_token\"}", true)]
    [InlineData(401, "{\"error\":\"invalid_token\"}", true)]
    [InlineData(400, "{\"error\":\"invalid_request\"}", false)]
    [InlineData(500, "{\"error\":\"invalid_token\"}", false)]
    [InlineData(400, "not-json", false)]
    public void RevokeRetryOnlyTreatsConclusiveInvalidTokenAsAlreadyRevoked(
        int statusCode,
        string responseBody,
        bool expected)
    {
        Assert.Equal(expected, GoogleOAuthService.IsConclusiveAlreadyRevoked(statusCode, responseBody));
    }

    private static string PropertyNames(Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index)
        => string.Join(',', index.Properties.Select(x => x.Name));

    [Fact]
    public void StoredTokenDecoderDistinguishesMissingFromUnreadablePayload()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Token:Frontend"] = new string('f', 64),
                ["Token:ProviderTokenEncryptionKey"] = new string('p', 64)
            })
            .Build();
        var tokenService = new TokenService(configuration);

        var missing = MySqlDataStore.DecodeStoredToken<TokenResponse>(null!, tokenService);
        var unreadable = MySqlDataStore.DecodeStoredToken<TokenResponse>("corrupt-token", tokenService);

        Assert.Equal(ProviderTokenLoadStatus.Missing, missing.Status);
        Assert.Equal(ProviderTokenLoadStatus.Unreadable, unreadable.Status);
        Assert.Equal("corrupt-token", unreadable.EncryptedPayload);
        Assert.NotNull(unreadable.Error);
    }
}

public sealed class GoogleAccountOperationCoordinatorTests
{
    private const ulong DiscordUserId = 9007199254740993UL;

    [Fact]
    public async Task SameUserIsSerializedAndLeaseDisposalReleasesExactlyOnce()
    {
        var coordinator = new GoogleAccountOperationCoordinator();
        var firstLease = await coordinator.AcquireAsync(DiscordUserId, CancellationToken.None);
        var secondAcquire = coordinator.AcquireAsync(DiscordUserId, CancellationToken.None).AsTask();

        Assert.False(secondAcquire.IsCompleted);
        Assert.Equal(1, coordinator.GateCount);

        firstLease.Dispose();
        var secondLease = await secondAcquire.WaitAsync(TimeSpan.FromSeconds(5));
        firstLease.Dispose();

        var thirdAcquire = coordinator.AcquireAsync(DiscordUserId, CancellationToken.None).AsTask();
        Assert.False(thirdAcquire.IsCompleted);

        secondLease.Dispose();
        var thirdLease = await thirdAcquire.WaitAsync(TimeSpan.FromSeconds(5));
        thirdLease.Dispose();

        Assert.Equal(0, coordinator.GateCount);
    }

    [Fact]
    public async Task CanceledWaiterDropsItsReferenceAndLastHolderEvictsGate()
    {
        var coordinator = new GoogleAccountOperationCoordinator();
        var holder = await coordinator.AcquireAsync(DiscordUserId, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiter = coordinator.AcquireAsync(DiscordUserId, cancellation.Token).AsTask();

        Assert.False(waiter.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiter);
        Assert.Equal(1, coordinator.GateCount);

        holder.Dispose();
        Assert.Equal(0, coordinator.GateCount);

        var replacement = await coordinator.AcquireAsync(DiscordUserId, CancellationToken.None);
        replacement.Dispose();
        Assert.Equal(0, coordinator.GateCount);
    }

    [Fact]
    public async Task ConcurrentAcquireReleaseCyclesSerializePerUserAndEvictAllGates()
    {
        const int keyCount = 16;
        const int operationsPerRound = 128;
        var coordinator = new GoogleAccountOperationCoordinator();
        var activeByKey = new int[keyCount];
        var overlapDetected = 0;

        for (var round = 0; round < 10; round++)
        {
            var tasks = Enumerable.Range(0, operationsPerRound).Select(async operation =>
            {
                var keyIndex = operation % keyCount;
                using var lease = await coordinator.AcquireAsync((ulong)keyIndex, CancellationToken.None);
                if (Interlocked.Increment(ref activeByKey[keyIndex]) != 1)
                    Interlocked.Exchange(ref overlapDetected, 1);

                try
                {
                    await Task.Yield();
                }
                finally
                {
                    Interlocked.Decrement(ref activeByKey[keyIndex]);
                }
            });

            await Task.WhenAll(tasks);
            Assert.Equal(0, coordinator.GateCount);
        }

        Assert.Equal(0, overlapDetected);
    }
}

public sealed class GoogleAccountLinkServiceTests
{
    private const ulong DiscordUserId = 9007199254740993UL;

    [Fact]
    public async Task LinkedAccountReturnsAllSubscriptionRows()
    {
        var rows = CreateRows();
        var store = new FakeGoogleAccountLinkStore(rows, cleanupPending: true);
        var service = CreateService(new FakeGoogleAccountProvider("linked"), store, new FakeWakeupPublisher());

        var result = await service.GetAccountLinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal("linked", result.Status);
        Assert.Equal(2, result.Subscriptions.Count);
        Assert.True(result.CleanupPending);
        Assert.False(store.LastPendingOnly);
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("invalid")]
    public async Task NonLinkedAccountReturnsPendingRowsWithoutChangingOAuthStatus(string status)
    {
        var store = new FakeGoogleAccountLinkStore(CreateRows(), cleanupPending: true);
        var service = CreateService(new FakeGoogleAccountProvider(status), store, new FakeWakeupPublisher());

        var result = await service.GetAccountLinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(status, result.Status);
        var row = Assert.Single(result.Subscriptions);
        Assert.True(row.PendingRoleRemoval);
        Assert.True(result.CleanupPending);
        Assert.True(store.LastPendingOnly);
    }

    [Fact]
    public async Task ProviderRevokeFailurePreservesCleanupIntentWithoutPublishing()
    {
        var events = new List<string>();
        var provider = new FakeGoogleAccountProvider("linked");
        var revoker = new FakeGoogleProviderRevoker(revokeSucceeded: false, events);
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: false, events);
        var publisher = new FakeWakeupPublisher(events);
        var service = CreateService(provider, revoker, store, publisher);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.ProviderRevokeFailed, result);
        Assert.Equal(["prepare", "revoke"], events);
        Assert.True(store.ChecksWereMarkedPending);
        Assert.Equal(1, store.PrepareCount);
        Assert.Equal(0, store.CompleteCount);
        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task UnreadableTokenPreservesCleanupIntentAndTokenWithoutPublishing()
    {
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true);
        var publisher = new FakeWakeupPublisher();
        var service = CreateService(
            new FakeGoogleAccountProvider("invalid"),
            new FakeGoogleProviderRevoker(GoogleProviderRevokeOutcome.TokenUnreadable),
            store,
            publisher);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.ProviderRevokeFailed, result);
        Assert.Equal(1, store.PrepareCount);
        Assert.Equal(0, store.CompleteCount);
        Assert.True(store.ChecksWereMarkedPending);
        Assert.Equal("stored-token", store.CurrentEncryptedToken);
        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task MissingTokenIsTreatedAsNoGrantAndStillCommitsPendingCleanup()
    {
        var store = new FakeGoogleAccountLinkStore(
            [],
            cleanupPending: true,
            currentEncryptedToken: null);
        var service = CreateService(
            new FakeGoogleAccountProvider("unlinked"),
            new FakeGoogleProviderRevoker(
                GoogleProviderRevokeOutcome.NoGrant,
                expectedEncryptedToken: null),
            store,
            new FakeWakeupPublisher());

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.CleanupPending, result);
        Assert.True(store.ChecksWereMarkedPending);
    }

    [Theory]
    [InlineData(false, GoogleUnlinkResult.Unlinked)]
    [InlineData(true, GoogleUnlinkResult.CleanupPending)]
    internal async Task SuccessfulRevokeTransitionsBeforePublish(
        bool cleanupPending,
        GoogleUnlinkResult expected)
    {
        var events = new List<string>();
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(revokeSucceeded: true, events),
            new FakeGoogleAccountLinkStore([], cleanupPending, events),
            new FakeWakeupPublisher(events));

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(["prepare", "revoke", "complete", "publish"], events);
    }

    [Fact]
    public async Task PublishFailureDoesNotChangeDurableUnlinkResult()
    {
        var events = new List<string>();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true, events);
        var publisher = new FakeWakeupPublisher(events) { Exception = new InvalidOperationException("redis unavailable") };
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(revokeSucceeded: true, events),
            store,
            publisher);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.CleanupPending, result);
        Assert.Equal(["prepare", "revoke", "complete", "publish"], events);
        Assert.Equal(1, store.PrepareCount);
        Assert.Equal(1, store.CompleteCount);
    }

    [Fact]
    public async Task ReplacementBetweenRevokeAndCommitReturnsConflictWithoutDeletingCleanupIntent()
    {
        var metrics = new FakeMetricsRefresher();
        var store = new FakeGoogleAccountLinkStore(
            [],
            cleanupPending: true,
            currentEncryptedToken: "replacement-token");
        var publisher = new FakeWakeupPublisher();
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(
                GoogleProviderRevokeOutcome.Revoked,
                expectedEncryptedToken: "revoked-token"),
            store,
            publisher,
            metrics);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.TokenChanged, result);
        Assert.Equal("replacement-token", store.CurrentEncryptedToken);
        Assert.True(store.ChecksWereMarkedPending);
        Assert.Equal(0, publisher.PublishCount);
        Assert.Equal(0, metrics.UpdateCount);
    }

    [Fact]
    public async Task CompletionFailureAfterProviderRevokeKeepsDurableCleanupIntentAndToken()
    {
        var events = new List<string>();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true, events)
        {
            CompleteException = new InvalidOperationException("database unavailable")
        };
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(revokeSucceeded: true, events),
            store,
            new FakeWakeupPublisher(events));

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.ProviderRevokeFailed, result);
        Assert.Equal(["prepare", "revoke", "complete"], events);
        Assert.True(store.ChecksWereMarkedPending);
        Assert.Equal("stored-token", store.CurrentEncryptedToken);
    }

    [Fact]
    public async Task DistributedLockContentionPreventsCleanupIntentAndProviderRevoke()
    {
        var revoker = new FakeGoogleProviderRevoker();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true);
        var distributedLock = new FakeGoogleOAuthOperationLock
        {
            AcquireStatus = GoogleOAuthOperationLockAcquireStatus.Contended
        };
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            revoker,
            store,
            new FakeWakeupPublisher(),
            distributedOperationLock: distributedLock);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.ProviderRevokeFailed, result);
        Assert.Equal(1, distributedLock.AcquireCount);
        Assert.Equal(0, store.PrepareCount);
        Assert.Equal(0, revoker.CallCount);
    }

    [Fact]
    public async Task DistributedLeaseSpansPreparationRevokeAndConditionalCompletion()
    {
        var distributedLock = new FakeGoogleOAuthOperationLock();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true)
        {
            IsDistributedLeaseHeld = () => distributedLock.Lease.IsHeld
        };
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(),
            store,
            new FakeWakeupPublisher(),
            distributedOperationLock: distributedLock);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.CleanupPending, result);
        Assert.True(store.PreparationObservedLeaseHeld);
        Assert.True(store.CompletionObservedLeaseHeld);
        Assert.False(distributedLock.Lease.IsHeld);
    }

    [Fact]
    public async Task OwnershipLostAfterProviderRevokeKeepsDurableFenceAndSkipsCompletion()
    {
        var distributedLock = new FakeGoogleOAuthOperationLock();
        distributedLock.Lease.OwnershipStatuses.Enqueue(GoogleOAuthOperationLockOwnershipStatus.Owned);
        distributedLock.Lease.OwnershipStatuses.Enqueue(GoogleOAuthOperationLockOwnershipStatus.OwnershipLost);
        var revoker = new FakeGoogleProviderRevoker();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: true);
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            revoker,
            store,
            new FakeWakeupPublisher(),
            distributedOperationLock: distributedLock);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.ProviderRevokeFailed, result);
        Assert.Equal(1, revoker.CallCount);
        Assert.True(store.ChecksWereMarkedPending);
        Assert.Equal(0, store.CompleteCount);
    }

    [Fact]
    public async Task CanceledRequestDoesNotCancelDestructiveUnlinkOperation()
    {
        using var requestCancellation = new CancellationTokenSource();
        requestCancellation.Cancel();
        var revoker = new FakeGoogleProviderRevoker();
        var store = new FakeGoogleAccountLinkStore([], cleanupPending: false);
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            revoker,
            store,
            new FakeWakeupPublisher());

        var result = await service.UnlinkAsync(DiscordUserId, requestCancellation.Token);

        Assert.Equal(GoogleUnlinkResult.Unlinked, result);
        Assert.NotEqual(requestCancellation.Token, revoker.LastCancellationToken);
        Assert.False(revoker.LastCancellationToken.IsCancellationRequested);
        Assert.Equal(revoker.LastCancellationToken, store.LastCancellationToken);
    }

    [Fact]
    public async Task UnlinkWaitsForSameUserCallbackCoordinatorLease()
    {
        var coordinator = new GoogleAccountOperationCoordinator();
        using var callbackLease = await coordinator.AcquireAsync(DiscordUserId, CancellationToken.None);
        var revoker = new FakeGoogleProviderRevoker();
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            revoker,
            new FakeGoogleAccountLinkStore([], cleanupPending: false),
            new FakeWakeupPublisher(),
            coordinator: coordinator);

        var unlinkTask = service.UnlinkAsync(DiscordUserId, CancellationToken.None);
        var firstCompleted = await Task.WhenAny(unlinkTask, Task.Delay(50));

        Assert.NotSame(unlinkTask, firstCompleted);
        Assert.Equal(0, revoker.CallCount);

        callbackLease.Dispose();
        Assert.Equal(GoogleUnlinkResult.Unlinked, await unlinkTask);
        Assert.Equal(1, revoker.CallCount);
    }

    [Fact]
    public async Task MetricsFailureAfterCommitDoesNotChangeDurableResultOrSkipWakeup()
    {
        var events = new List<string>();
        var metrics = new FakeMetricsRefresher(events)
        {
            Exception = new InvalidOperationException("metrics unavailable")
        };
        var service = CreateService(
            new FakeGoogleAccountProvider("linked"),
            new FakeGoogleProviderRevoker(revokeSucceeded: true, events),
            new FakeGoogleAccountLinkStore([], cleanupPending: true, events),
            new FakeWakeupPublisher(events),
            metrics);

        var result = await service.UnlinkAsync(DiscordUserId, CancellationToken.None);

        Assert.Equal(GoogleUnlinkResult.CleanupPending, result);
        Assert.Equal(["prepare", "revoke", "complete", "publish", "metrics"], events);
    }

    [Fact]
    public void ProductionUnlinkCancellationIsServerOwnedAndBounded()
    {
        var factory = new GoogleUnlinkOperationCancellationFactory();
        using var cancellation = factory.Create();

        Assert.True(cancellation.Token.CanBeCanceled);
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(30), GoogleUnlinkOperationCancellationFactory.OperationTimeout);
    }

    [Theory]
    [InlineData(GoogleUnlinkResult.Unlinked, 200)]
    [InlineData(GoogleUnlinkResult.CleanupPending, 202)]
    [InlineData(GoogleUnlinkResult.ProviderRevokeFailed, 503)]
    [InlineData(GoogleUnlinkResult.TokenChanged, 409)]
    internal void ControllerMapsGoogleUnlinkResultToExpectedStatus(
        GoogleUnlinkResult result,
        int expectedStatus)
    {
        var action = AccountLinksController.CreateGoogleUnlinkResult(result);
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(action);

        Assert.Equal(expectedStatus, objectResult.StatusCode);
        if (objectResult.Value is GoogleUnlinkResponse response)
        {
            Assert.Equal("unlinked", response.Status);
            Assert.Equal(result == GoogleUnlinkResult.CleanupPending, response.CleanupPending);
            var json = JObject.Parse(JsonConvert.SerializeObject(response));
            Assert.Equal(["cleanupPending", "status"],
                json.Properties().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        }
        else if (result == GoogleUnlinkResult.ProviderRevokeFailed)
        {
            Assert.NotNull(objectResult.Value);
            var json = JObject.FromObject(objectResult.Value!);
            Assert.Equal("google_revoke_failed", json.Value<string>("error"));
        }
        else
        {
            Assert.NotNull(objectResult.Value);
            var json = JObject.FromObject(objectResult.Value!);
            Assert.Equal("google_token_changed", json.Value<string>("error"));
            Assert.True(json.Value<bool>("retryable"));
        }
    }

    private static GoogleAccountLinkService CreateService(
        IGoogleAccountProvider provider,
        IGoogleAccountLinkStore store,
        IGoogleMemberCleanupWakeupPublisher publisher)
        => CreateService(provider, new FakeGoogleProviderRevoker(), store, publisher);

    private static GoogleAccountLinkService CreateService(
        IGoogleAccountProvider provider,
        IGoogleProviderRevoker revoker,
        IGoogleAccountLinkStore store,
        IGoogleMemberCleanupWakeupPublisher publisher,
        IGoogleAccountLinkMetricsRefresher? metricsRefresher = null,
        GoogleAccountOperationCoordinator? coordinator = null,
        IGoogleOAuthOperationLock? distributedOperationLock = null,
        IGoogleUnlinkOperationCancellationFactory? cancellationFactory = null)
        => new(
            provider,
            revoker,
            store,
            publisher,
            metricsRefresher ?? new FakeMetricsRefresher(),
            coordinator ?? new GoogleAccountOperationCoordinator(),
            distributedOperationLock ?? new FakeGoogleOAuthOperationLock(),
            cancellationFactory ?? new FakeUnlinkOperationCancellationFactory(),
            NullLogger<GoogleAccountLinkService>.Instance);

    private static IReadOnlyList<GoogleMemberSubscription> CreateRows()
        =>
        [
            new GoogleMemberSubscription
            {
                GuildId = "9007199254740993",
                ChannelId = "UC-active",
                IsChecked = true
            },
            new GoogleMemberSubscription
            {
                GuildId = "18446744073709551615",
                ChannelId = "UC-pending",
                PendingRoleRemoval = true
            }
        ];

    private sealed class FakeGoogleAccountProvider : IGoogleAccountProvider
    {
        private readonly string _status;

        public FakeGoogleAccountProvider(string status)
        {
            _status = status;
        }

        public Task<GoogleAccountLink> GetProviderAccountAsync(
            ulong discordUserId,
            CancellationToken cancellationToken)
            => Task.FromResult(new GoogleAccountLink { Status = _status });

    }

    private sealed class FakeGoogleProviderRevoker : IGoogleProviderRevoker
    {
        private readonly List<string>? _events;
        private readonly string? _expectedEncryptedToken;
        private readonly GoogleProviderRevokeOutcome _outcome;

        public FakeGoogleProviderRevoker(
            bool revokeSucceeded = true,
            List<string>? events = null)
            : this(
                revokeSucceeded ? GoogleProviderRevokeOutcome.Revoked : GoogleProviderRevokeOutcome.Failed,
                events,
                revokeSucceeded ? "stored-token" : null)
        {
        }

        public FakeGoogleProviderRevoker(
            GoogleProviderRevokeOutcome outcome,
            List<string>? events = null,
            string? expectedEncryptedToken = "stored-token")
        {
            _outcome = outcome;
            _events = events;
            _expectedEncryptedToken = expectedEncryptedToken;
        }

        public CancellationToken LastCancellationToken { get; private set; }
        public int CallCount { get; private set; }

        public Task<GoogleProviderRevokeResult> RevokeAsync(
            ulong discordUserId,
            string expectedEncryptedToken,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastCancellationToken = cancellationToken;
            _events?.Add("revoke");
            return Task.FromResult(new GoogleProviderRevokeResult(_outcome, _expectedEncryptedToken));
        }
    }

    private sealed class FakeGoogleAccountLinkStore : IGoogleAccountLinkStore
    {
        private readonly bool _cleanupPending;
        private readonly List<string>? _events;
        private readonly IReadOnlyList<GoogleMemberSubscription> _rows;

        public FakeGoogleAccountLinkStore(
            IReadOnlyList<GoogleMemberSubscription> rows,
            bool cleanupPending,
            List<string>? events = null,
            string? currentEncryptedToken = "stored-token")
        {
            _rows = rows;
            _cleanupPending = cleanupPending;
            _events = events;
            CurrentEncryptedToken = currentEncryptedToken;
        }

        public bool ChecksWereMarkedPending { get; private set; }
        public string? CurrentEncryptedToken { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public bool LastPendingOnly { get; private set; }
        public int PrepareCount { get; private set; }
        public int CompleteCount { get; private set; }
        public Exception? CompleteException { get; init; }
        public Func<bool>? IsDistributedLeaseHeld { get; init; }
        public bool PreparationObservedLeaseHeld { get; private set; }
        public bool CompletionObservedLeaseHeld { get; private set; }

        public Task<IReadOnlyList<GoogleMemberSubscription>> GetSubscriptionsAsync(
            ulong discordUserId,
            bool pendingOnly,
            CancellationToken cancellationToken)
        {
            LastPendingOnly = pendingOnly;
            IReadOnlyList<GoogleMemberSubscription> rows = pendingOnly
                ? _rows.Where(x => x.PendingRoleRemoval).ToList()
                : _rows;
            return Task.FromResult(rows);
        }

        public Task<GoogleUnlinkPreparation> PreparePendingCleanupAsync(
            ulong discordUserId,
            CancellationToken cancellationToken)
        {
            PrepareCount++;
            LastCancellationToken = cancellationToken;
            _events?.Add("prepare");
            PreparationObservedLeaseHeld = IsDistributedLeaseHeld?.Invoke() == true;
            ChecksWereMarkedPending = true;
            return Task.FromResult(new GoogleUnlinkPreparation(CurrentEncryptedToken, _cleanupPending));
        }

        public Task<GoogleUnlinkTransitionOutcome> CompletePendingCleanupAsync(
            ulong discordUserId,
            string expectedEncryptedToken,
            CancellationToken cancellationToken)
        {
            CompleteCount++;
            LastCancellationToken = cancellationToken;
            _events?.Add("complete");
            CompletionObservedLeaseHeld = IsDistributedLeaseHeld?.Invoke() == true;
            if (CompleteException != null)
                return Task.FromException<GoogleUnlinkTransitionOutcome>(CompleteException);
            if (!string.Equals(CurrentEncryptedToken, expectedEncryptedToken, StringComparison.Ordinal))
                return Task.FromResult(GoogleUnlinkTransitionOutcome.TokenChanged);

            CurrentEncryptedToken = null;
            return Task.FromResult(GoogleUnlinkTransitionOutcome.Committed);
        }
    }

    private sealed class FakeMetricsRefresher : IGoogleAccountLinkMetricsRefresher
    {
        private readonly List<string>? _events;

        public FakeMetricsRefresher(List<string>? events = null)
        {
            _events = events;
        }

        public Exception? Exception { get; init; }
        public int UpdateCount { get; private set; }

        public Task UpdateMetricsAsync(CancellationToken cancellationToken)
        {
            UpdateCount++;
            _events?.Add("metrics");
            return Exception == null
                ? Task.CompletedTask
                : Task.FromException(Exception);
        }
    }

    private sealed class FakeGoogleOAuthOperationLock : IGoogleOAuthOperationLock
    {
        public GoogleOAuthOperationLockAcquireStatus AcquireStatus { get; init; } =
            GoogleOAuthOperationLockAcquireStatus.Acquired;
        public int AcquireCount { get; private set; }
        public FakeGoogleOAuthOperationLockLease Lease { get; } = new();

        public Task<GoogleOAuthOperationLockAcquireResult> TryAcquireAsync(
            ulong discordUserId,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (AcquireStatus == GoogleOAuthOperationLockAcquireStatus.Acquired)
            {
                Lease.IsHeld = true;
                return Task.FromResult(GoogleOAuthOperationLockAcquireResult.Acquired(Lease));
            }

            return Task.FromResult(AcquireStatus == GoogleOAuthOperationLockAcquireStatus.Contended
                ? GoogleOAuthOperationLockAcquireResult.Contended()
                : GoogleOAuthOperationLockAcquireResult.TemporaryFailure(
                    new InvalidOperationException("redis unavailable")));
        }
    }

    private sealed class FakeGoogleOAuthOperationLockLease : IGoogleOAuthOperationLockLease
    {
        public bool IsHeld { get; set; }
        public GoogleOAuthOperationLockOwnershipStatus OwnershipStatus { get; init; } =
            GoogleOAuthOperationLockOwnershipStatus.Owned;
        public Queue<GoogleOAuthOperationLockOwnershipStatus> OwnershipStatuses { get; } = new();

        public Task<GoogleOAuthOperationLockOwnershipStatus> EnsureOwnedAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OwnershipStatuses.Count > 0
                ? OwnershipStatuses.Dequeue()
                : OwnershipStatus);
        }

        public ValueTask DisposeAsync()
        {
            IsHeld = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeUnlinkOperationCancellationFactory : IGoogleUnlinkOperationCancellationFactory
    {
        public CancellationTokenSource Create()
            => new(TimeSpan.FromMinutes(1));
    }

    private sealed class FakeWakeupPublisher : IGoogleMemberCleanupWakeupPublisher
    {
        private readonly List<string>? _events;

        public FakeWakeupPublisher(List<string>? events = null)
        {
            _events = events;
        }

        public Exception? Exception { get; init; }
        public int PublishCount { get; private set; }

        public ValueTask PublishAsync(ulong discordUserId, CancellationToken cancellationToken)
        {
            PublishCount++;
            _events?.Add("publish");
            return Exception == null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(Exception);
        }
    }
}
