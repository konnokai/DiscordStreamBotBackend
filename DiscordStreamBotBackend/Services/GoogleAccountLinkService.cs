using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

internal interface IGoogleAccountProvider
{
    Task<GoogleAccountLink> GetProviderAccountAsync(ulong discordUserId, CancellationToken cancellationToken);
}

internal interface IGoogleProviderRevoker
{
    Task<GoogleProviderRevokeResult> RevokeAsync(
        ulong discordUserId,
        string expectedEncryptedToken,
        CancellationToken cancellationToken);
}

internal interface IGoogleAccountLinkStore
{
    Task<IReadOnlyList<GoogleMemberSubscription>> GetSubscriptionsAsync(
        ulong discordUserId,
        bool pendingOnly,
        CancellationToken cancellationToken);

    Task<GoogleUnlinkPreparation> PreparePendingCleanupAsync(
        ulong discordUserId,
        CancellationToken cancellationToken);

    Task<GoogleUnlinkTransitionOutcome> CompletePendingCleanupAsync(
        ulong discordUserId,
        string expectedEncryptedToken,
        CancellationToken cancellationToken);
}

internal interface IGoogleMemberCleanupWakeupPublisher
{
    ValueTask PublishAsync(ulong discordUserId, CancellationToken cancellationToken);
}

internal interface IGoogleAccountLinkMetricsRefresher
{
    Task UpdateMetricsAsync(CancellationToken cancellationToken);
}

internal interface IGoogleUnlinkOperationCancellationFactory
{
    CancellationTokenSource Create();
}

internal enum GoogleProviderRevokeOutcome
{
    Revoked,
    NoGrant,
    TokenChanged,
    TokenUnreadable,
    Failed
}

internal readonly record struct GoogleProviderRevokeResult(
    GoogleProviderRevokeOutcome Outcome,
    string ExpectedEncryptedToken);

internal enum GoogleUnlinkTransitionOutcome
{
    Committed,
    TokenChanged
}

internal readonly record struct GoogleUnlinkPreparation(
    string ExpectedEncryptedToken,
    bool CleanupPending);

internal enum GoogleUnlinkResult
{
    ProviderRevokeFailed,
    TokenChanged,
    Unlinked,
    CleanupPending
}

public sealed class GoogleAccountOperationCoordinator
{
    private readonly Dictionary<ulong, GateEntry> _gates = new();
    private readonly object _sync = new();

    // 這個 gate 只處理單一程序內的排隊與 callback 共用；跨程序互斥交給 GoogleOAuthOperationLock，
    // 分散式 lease 外再用加密 Token 的 CAS 防止新 Token 被舊操作覆蓋。
    internal int GateCount
    {
        get
        {
            lock (_sync)
                return _gates.Count;
        }
    }

    public async ValueTask<IDisposable> AcquireAsync(
        ulong discordUserId,
        CancellationToken cancellationToken)
    {
        GateEntry entry;
        lock (_sync)
        {
            if (!_gates.TryGetValue(discordUserId, out entry))
            {
                entry = new GateEntry();
                _gates.Add(discordUserId, entry);
            }

            // 參照數包含目前持有者與排隊中的等待者。註冊與移除指定項目使用同一把鎖，
            // 確保取得 gate 時不會掛到已移除的項目。
            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
            return new Releaser(this, discordUserId, entry);
        }
        catch
        {
            ReleaseReference(discordUserId, entry);
            throw;
        }
    }

    private void Release(ulong discordUserId, GateEntry entry)
    {
        try
        {
            entry.Semaphore.Release();
        }
        finally
        {
            ReleaseReference(discordUserId, entry);
        }
    }

    private void ReleaseReference(ulong discordUserId, GateEntry entry)
    {
        SemaphoreSlim semaphoreToDispose = null;
        lock (_sync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                _gates.TryGetValue(discordUserId, out var current) &&
                ReferenceEquals(current, entry))
            {
                _gates.Remove(discordUserId);
                semaphoreToDispose = entry.Semaphore;
            }
        }

        semaphoreToDispose?.Dispose();
    }

    private sealed class GateEntry
    {
        public int ReferenceCount { get; set; }
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
    }

    private sealed class Releaser : IDisposable
    {
        private GoogleAccountOperationCoordinator _coordinator;
        private readonly ulong _discordUserId;
        private readonly GateEntry _entry;

        public Releaser(
            GoogleAccountOperationCoordinator coordinator,
            ulong discordUserId,
            GateEntry entry)
        {
            _coordinator = coordinator;
            _discordUserId = discordUserId;
            _entry = entry;
        }

        public void Dispose()
        {
            var coordinator = Interlocked.Exchange(ref _coordinator, null);
            coordinator?.Release(_discordUserId, _entry);
        }
    }
}

internal sealed class GoogleUnlinkOperationCancellationFactory : IGoogleUnlinkOperationCancellationFactory
{
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    public CancellationTokenSource Create()
        => new(OperationTimeout);
}

public sealed class GoogleAccountLinkService
{
    private readonly IGoogleAccountProvider _accountProvider;
    private readonly IGoogleAccountLinkStore _accountLinkStore;
    private readonly GoogleAccountOperationCoordinator _coordinator;
    private readonly IGoogleOAuthOperationLock _distributedOperationLock;
    private readonly ILogger<GoogleAccountLinkService> _logger;
    private readonly IGoogleAccountLinkMetricsRefresher _metricsRefresher;
    private readonly IGoogleUnlinkOperationCancellationFactory _operationCancellationFactory;
    private readonly IGoogleProviderRevoker _providerRevoker;
    private readonly IGoogleMemberCleanupWakeupPublisher _wakeupPublisher;

    internal GoogleAccountLinkService(
        IGoogleAccountProvider accountProvider,
        IGoogleProviderRevoker providerRevoker,
        IGoogleAccountLinkStore accountLinkStore,
        IGoogleMemberCleanupWakeupPublisher wakeupPublisher,
        IGoogleAccountLinkMetricsRefresher metricsRefresher,
        GoogleAccountOperationCoordinator coordinator,
        IGoogleOAuthOperationLock distributedOperationLock,
        IGoogleUnlinkOperationCancellationFactory operationCancellationFactory,
        ILogger<GoogleAccountLinkService> logger)
    {
        _accountProvider = accountProvider;
        _providerRevoker = providerRevoker;
        _accountLinkStore = accountLinkStore;
        _wakeupPublisher = wakeupPublisher;
        _metricsRefresher = metricsRefresher;
        _coordinator = coordinator;
        _distributedOperationLock = distributedOperationLock;
        _operationCancellationFactory = operationCancellationFactory;
        _logger = logger;
    }

    public async Task<GoogleAccountLink> GetAccountLinkAsync(
        ulong discordUserId,
        CancellationToken cancellationToken)
    {
        var account = await _accountProvider.GetProviderAccountAsync(discordUserId, cancellationToken);
        var subscriptions = await _accountLinkStore.GetSubscriptionsAsync(
            discordUserId,
            pendingOnly: account.Status != "linked",
            cancellationToken);

        account.Subscriptions = subscriptions;
        account.CleanupPending = subscriptions.Any(x => x.PendingRoleRemoval);
        return account;
    }

    internal async Task<GoogleUnlinkResult> UnlinkAsync(
        ulong discordUserId,
        CancellationToken requestCancellationToken)
    {
        // RequestAborted 只負責中止驗證前的請求；開始撤銷後改用伺服器持有的逾時，避免用戶端在
        // Google 接受撤銷與本機 transaction 之間斷線。
        _ = requestCancellationToken;
        using var operationCancellation = _operationCancellationFactory.Create();
        var operationToken = operationCancellation.Token;

        try
        {
            using var operationLease = await _coordinator.AcquireAsync(discordUserId, operationToken);
            GoogleOAuthOperationLockAcquireResult lockResult = await _distributedOperationLock.TryAcquireAsync(
                discordUserId, operationToken);
            if (lockResult.Status != GoogleOAuthOperationLockAcquireStatus.Acquired)
            {
                _logger.LogWarning(
                    lockResult.Exception,
                    "Google 解除連結無法取得跨程序 OAuth lease；DiscordUserId: {DiscordUserId}；Status: {Status}",
                    discordUserId,
                    lockResult.Status);
                return GoogleUnlinkResult.ProviderRevokeFailed;
            }
            await using var distributedLease = lockResult.Lease;

            // 先儲存角色清理意圖；即使 Google 撤銷成功後程序中斷，排程仍可依這筆資料繼續處理。
            var preparation = await _accountLinkStore.PreparePendingCleanupAsync(
                discordUserId,
                operationToken);
            if (await distributedLease.EnsureOwnedAsync(operationToken) !=
                GoogleOAuthOperationLockOwnershipStatus.Owned)
            {
                return GoogleUnlinkResult.ProviderRevokeFailed;
            }
            var revokeResult = await _providerRevoker.RevokeAsync(
                discordUserId,
                preparation.ExpectedEncryptedToken,
                operationToken);
            if (revokeResult.Outcome == GoogleProviderRevokeOutcome.TokenChanged)
                return GoogleUnlinkResult.TokenChanged;
            if (revokeResult.Outcome is GoogleProviderRevokeOutcome.TokenUnreadable or GoogleProviderRevokeOutcome.Failed)
                return GoogleUnlinkResult.ProviderRevokeFailed;

            if (await distributedLease.EnsureOwnedAsync(operationToken) !=
                GoogleOAuthOperationLockOwnershipStatus.Owned)
            {
                return GoogleUnlinkResult.ProviderRevokeFailed;
            }

            var transition = await _accountLinkStore.CompletePendingCleanupAsync(
                discordUserId,
                revokeResult.ExpectedEncryptedToken,
                operationToken);
            if (transition == GoogleUnlinkTransitionOutcome.TokenChanged)
                return GoogleUnlinkResult.TokenChanged;

            await TryPublishWakeupAsync(discordUserId, operationToken);
            await TryUpdateMetricsAsync(operationToken);

            return preparation.CleanupPending
                ? GoogleUnlinkResult.CleanupPending
                : GoogleUnlinkResult.Unlinked;
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Google 解除連結超過伺服器作業時間限制 | DiscordUserId: {DiscordUserId}",
                discordUserId);
            return GoogleUnlinkResult.ProviderRevokeFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google 解除連結失敗 | DiscordUserId: {DiscordUserId}", discordUserId);
            return GoogleUnlinkResult.ProviderRevokeFailed;
        }
    }

    private async Task TryUpdateMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _metricsRefresher.UpdateMetricsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google 解除連結已寫入資料庫，但 OAuth 帳號指標更新失敗");
        }
    }

    private async Task TryPublishWakeupAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        try
        {
            await _wakeupPublisher.PublishAsync(discordUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            BackendMetrics.GoogleCleanupWakeupPublishFailures.Inc();
            _logger.LogWarning(
                ex,
                "Google 解除連結已寫入資料庫，但 Redis 角色清理喚醒通知失敗 | DiscordUserId: {DiscordUserId}",
                discordUserId);
        }
    }
}

internal sealed class GoogleAccountLinkStore : IGoogleAccountLinkStore
{
    private readonly IDbContextFactory<MainDbContext> _dbContextFactory;

    public GoogleAccountLinkStore(IDbContextFactory<MainDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<GoogleMemberSubscription>> GetSubscriptionsAsync(
        ulong discordUserId,
        bool pendingOnly,
        CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var query = db.YoutubeMemberCheck.AsNoTracking().Where(x => x.UserId == discordUserId);
        if (pendingOnly)
            query = query.Where(x => x.PendingRoleRemoval);

        var rows = await query
            .Select(x => new
            {
                x.GuildId,
                ChannelId = x.CheckYtChannelId,
                x.IsChecked,
                x.PendingRoleRemoval,
                LastCheckedAt = x.LastCheckTime
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new GoogleMemberSubscription
        {
            GuildId = x.GuildId.ToString(CultureInfo.InvariantCulture),
            ChannelId = x.ChannelId,
            IsChecked = x.IsChecked,
            PendingRoleRemoval = x.PendingRoleRemoval,
            LastCheckedAt = x.LastCheckedAt
        }).ToList();
    }

    public async Task<GoogleUnlinkPreparation> PreparePendingCleanupAsync(
        ulong discordUserId,
        CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        // 使用 Serializable 讓 Token 快照與清理意圖在同一個可持久化檢查點中完成。
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var expectedEncryptedToken = await db.YoutubeMemberAccessToken.AsNoTracking()
            .Where(x => x.DiscordUserId == discordUserId)
            .Select(x => x.EncryptedAccessToken)
            .SingleOrDefaultAsync(cancellationToken);
        var intent = await db.GoogleOAuthUnlinkIntent.SingleOrDefaultAsync(
            x => x.DiscordUserId == discordUserId,
            cancellationToken);
        if (intent == null)
        {
            db.GoogleOAuthUnlinkIntent.Add(new GoogleOAuthUnlinkIntent
            {
                DiscordUserId = discordUserId,
                ExpectedEncryptedToken = expectedEncryptedToken,
                DateAdded = DateTime.UtcNow
            });
        }
        else
        {
            intent.ExpectedEncryptedToken = expectedEncryptedToken;
            intent.DateAdded = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.YoutubeMemberCheck
            .Where(x => x.UserId == discordUserId)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.IsChecked, false)
                .SetProperty(x => x.PendingRoleRemoval, true), cancellationToken);
        var cleanupPending = await db.YoutubeMemberCheck.AsNoTracking()
            .AnyAsync(x => x.UserId == discordUserId && x.PendingRoleRemoval, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new GoogleUnlinkPreparation(expectedEncryptedToken, cleanupPending);
    }

    public async Task<GoogleUnlinkTransitionOutcome> CompletePendingCleanupAsync(
        ulong discordUserId,
        string expectedEncryptedToken,
        CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        // Serializable 加上主鍵查詢，可防止原本沒有 Token 時被其他程序同時插入；
        // 已有 Token 的撤銷則另外用加密內容做 CAS。
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        if (expectedEncryptedToken == null)
        {
            if (await db.YoutubeMemberAccessToken
                .AnyAsync(x => x.DiscordUserId == discordUserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return GoogleUnlinkTransitionOutcome.TokenChanged;
            }
        }
        else
        {
            // Token 密文與 HMAC 區分大小寫。使用 BINARY 比對，避免資料庫不分大小寫的排序規則接受
            // 與送給 Google 撤銷端點不完全相同的內容。
            var deleted = await db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM `youtube_member_access_token`
WHERE `discord_user_id` = {discordUserId}
  AND BINARY `encrypted_access_token` = BINARY {expectedEncryptedToken}", cancellationToken);
            if (deleted != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return GoogleUnlinkTransitionOutcome.TokenChanged;
            }
        }

        await db.GoogleOAuthUnlinkIntent
            .Where(x => x.DiscordUserId == discordUserId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return GoogleUnlinkTransitionOutcome.Committed;
    }
}

internal sealed class GoogleMemberCleanupWakeupPublisher : IGoogleMemberCleanupWakeupPublisher
{
    private readonly RedisService _redisService;

    public GoogleMemberCleanupWakeupPublisher(RedisService redisService)
    {
        _redisService = redisService;
    }

    public ValueTask PublishAsync(ulong discordUserId, CancellationToken cancellationToken)
        => _redisService.AddPubMessageAsync(
            RedisChannels.Member.RevokeToken,
            GetPayload(discordUserId),
            cancellationToken);

    internal static string GetPayload(ulong discordUserId)
        => discordUserId.ToString(CultureInfo.InvariantCulture);
}
