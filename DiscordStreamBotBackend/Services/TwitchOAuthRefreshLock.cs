using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

internal enum TwitchOAuthRefreshLockAcquireStatus
{
    Acquired,
    Contended,
    TemporaryFailure
}

internal enum TwitchOAuthRefreshLockOwnershipStatus
{
    Owned,
    OwnershipLost,
    TemporaryFailure
}

internal enum TwitchOAuthRefreshLockReleaseStatus
{
    Released,
    OwnershipLost,
    TemporaryFailure
}

internal sealed class TwitchOAuthRefreshLockAcquireResult
{
    public TwitchOAuthRefreshLockAcquireStatus Status { get; private init; }
    public TwitchOAuthRefreshLockLease Lease { get; private init; }
    public Exception Exception { get; private init; }

    public static TwitchOAuthRefreshLockAcquireResult Acquired(TwitchOAuthRefreshLockLease lease)
        => new() { Status = TwitchOAuthRefreshLockAcquireStatus.Acquired, Lease = lease };

    public static TwitchOAuthRefreshLockAcquireResult Contended()
        => new() { Status = TwitchOAuthRefreshLockAcquireStatus.Contended };

    public static TwitchOAuthRefreshLockAcquireResult TemporaryFailure(Exception exception)
        => new() { Status = TwitchOAuthRefreshLockAcquireStatus.TemporaryFailure, Exception = exception };
}

internal sealed class TwitchOAuthRefreshLock
{
    internal const string KeyPrefix = "twitch:oauth:refresh-lock:";
    internal static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    private readonly IDatabase _database;

    public TwitchOAuthRefreshLock(IDatabase database)
    {
        RedisService.ValidateProviderStateDatabaseIndex(database.Database);
        _database = database;
    }

    /// <summary>依 Twitch user ID 嘗試取得跨 Bot/Backend 共用且可續租的 refresh lease。</summary>
    public async Task<TwitchOAuthRefreshLockAcquireResult> TryAcquireAsync(
        string twitchUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(twitchUserId))
            throw new ArgumentException("Twitch user ID is required.", nameof(twitchUserId));

        cancellationToken.ThrowIfCancellationRequested();
        var key = GetKey(twitchUserId);
        var owner = $"backend:{Environment.ProcessId}:{Guid.NewGuid():N}";

        try
        {
            var acquired = await _database.StringSetAsync(key, owner, DefaultTtl, When.NotExists);
            return acquired
                ? TwitchOAuthRefreshLockAcquireResult.Acquired(
                    new TwitchOAuthRefreshLockLease(_database, key, owner, DefaultTtl))
                : TwitchOAuthRefreshLockAcquireResult.Contended();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException)
        {
            return TwitchOAuthRefreshLockAcquireResult.TemporaryFailure(ex);
        }
    }

    internal static string GetKey(string twitchUserId) => $"{KeyPrefix}{twitchUserId}";
}

internal sealed class TwitchOAuthRefreshLockLease : IAsyncDisposable
{
    // TTL 到期後可能已有新 owner 接手；續租與釋放必須在 Redis 內原子比對 owner。
    // 舊持有者不得延長或刪除其他程序重新取得的 lock。
    private const string RenewScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";
    private const string ReleaseScript = "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";
    private readonly IDatabase _database;
    private readonly RedisKey _key;
    private readonly RedisValue _owner;
    private readonly TimeSpan _ttl;
    private readonly CancellationTokenSource _renewalCancellation = new();
    private readonly Task _renewalTask;
    private int _ownershipLost;
    private int _releaseStarted;

    public TwitchOAuthRefreshLockLease(
        IDatabase database,
        RedisKey key,
        RedisValue owner,
        TimeSpan ttl)
    {
        _database = database;
        _key = key;
        _owner = owner;
        _ttl = ttl;
        _renewalTask = RenewUntilReleasedAsync(_renewalCancellation.Token);
    }

    /// <summary>原子確認 owner 並延長 TTL；ownership lost 後禁止持有者再執行 authority-changing write。</summary>
    public async Task<(TwitchOAuthRefreshLockOwnershipStatus Status, Exception Exception)> EnsureOwnedAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _ownershipLost) != 0)
            return (TwitchOAuthRefreshLockOwnershipStatus.OwnershipLost, null);

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await _database.ScriptEvaluateAsync(
                RenewScript,
                [_key],
                [_owner, (long)_ttl.TotalMilliseconds]);
            cancellationToken.ThrowIfCancellationRequested();
            if ((long)result == 1)
                return (TwitchOAuthRefreshLockOwnershipStatus.Owned, null);

            Interlocked.Exchange(ref _ownershipLost, 1);
            return (TwitchOAuthRefreshLockOwnershipStatus.OwnershipLost, null);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException)
        {
            return (TwitchOAuthRefreshLockOwnershipStatus.TemporaryFailure, ex);
        }
    }

    /// <summary>停止續租並僅在 owner 相符時刪除 Redis lock，避免舊 lease 刪除新 owner。</summary>
    public async Task<(TwitchOAuthRefreshLockReleaseStatus Status, Exception Exception)> ReleaseAsync(
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _releaseStarted, 1) != 0)
            return (TwitchOAuthRefreshLockReleaseStatus.Released, null);

        await StopRenewalAsync();
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _database.ScriptEvaluateAsync(
                ReleaseScript,
                [_key],
                [_owner]);
            cancellationToken.ThrowIfCancellationRequested();
            return ((long)result == 1
                ? TwitchOAuthRefreshLockReleaseStatus.Released
                : TwitchOAuthRefreshLockReleaseStatus.OwnershipLost, null);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException)
        {
            return (TwitchOAuthRefreshLockReleaseStatus.TemporaryFailure, ex);
        }
    }

    private async Task RenewUntilReleasedAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_ttl.TotalMilliseconds / 3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var result = await EnsureOwnedAsync(cancellationToken);
                if (result.Status == TwitchOAuthRefreshLockOwnershipStatus.OwnershipLost)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopRenewalAsync()
    {
        if (!_renewalCancellation.IsCancellationRequested)
            _renewalCancellation.Cancel();

        try
        {
            await _renewalTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Redis call may still be completing. Renewal is cancelled and the lock is bounded by its TTL.
        }

        if (_renewalTask.IsCompleted)
            _renewalCancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }
}
