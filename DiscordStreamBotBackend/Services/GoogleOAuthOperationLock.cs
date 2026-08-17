using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public enum GoogleOAuthOperationLockAcquireStatus
{
    Acquired,
    Contended,
    TemporaryFailure
}

public enum GoogleOAuthOperationLockOwnershipStatus
{
    Owned,
    OwnershipLost,
    TemporaryFailure
}

public interface IGoogleOAuthOperationLock
{
    Task<GoogleOAuthOperationLockAcquireResult> TryAcquireAsync(
        ulong discordUserId,
        CancellationToken cancellationToken);
}

public interface IGoogleOAuthOperationLockLease : IAsyncDisposable
{
    Task<GoogleOAuthOperationLockOwnershipStatus> EnsureOwnedAsync(CancellationToken cancellationToken);
}

public readonly record struct GoogleOAuthOperationLockAcquireResult(
    GoogleOAuthOperationLockAcquireStatus Status,
    IGoogleOAuthOperationLockLease Lease,
    Exception Exception)
{
    public static GoogleOAuthOperationLockAcquireResult Acquired(IGoogleOAuthOperationLockLease lease)
        => new(GoogleOAuthOperationLockAcquireStatus.Acquired, lease, null);

    public static GoogleOAuthOperationLockAcquireResult Contended()
        => new(GoogleOAuthOperationLockAcquireStatus.Contended, null, null);

    public static GoogleOAuthOperationLockAcquireResult TemporaryFailure(Exception exception)
        => new(GoogleOAuthOperationLockAcquireStatus.TemporaryFailure, null, exception);
}

/// <summary>
/// Google OAuth 帳號異動使用跨程序 lease。callback、refresh、unlink 共用同一個 key，
/// 避免其他程序在 provider 撤銷期間寫入替代 Token。
/// </summary>
internal sealed class GoogleOAuthOperationLock : IGoogleOAuthOperationLock
{
    internal static readonly TimeSpan DefaultTtl = TwitchOAuthRefreshLock.DefaultTtl;
    private readonly IDatabase _database;

    public GoogleOAuthOperationLock(IDatabase database)
    {
        RedisService.ValidateProviderStateDatabaseIndex(database.Database);
        _database = database;
    }

    public async Task<GoogleOAuthOperationLockAcquireResult> TryAcquireAsync(
        ulong discordUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RedisKey key = RedisChannels.OAuth.GoogleOperationLock(discordUserId);
        RedisValue owner = $"backend:{Environment.ProcessId}:{Guid.NewGuid():N}";

        try
        {
            return await _database.StringSetAsync(key, owner, DefaultTtl, When.NotExists)
                ? GoogleOAuthOperationLockAcquireResult.Acquired(
                    new GoogleOAuthOperationLockLease(_database, key, owner, DefaultTtl))
                : GoogleOAuthOperationLockAcquireResult.Contended();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException)
        {
            return GoogleOAuthOperationLockAcquireResult.TemporaryFailure(ex);
        }
    }
}

internal sealed class GoogleOAuthOperationLockLease : IGoogleOAuthOperationLockLease
{
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

    public GoogleOAuthOperationLockLease(
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

    public async Task<GoogleOAuthOperationLockOwnershipStatus> EnsureOwnedAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _ownershipLost) != 0)
            return GoogleOAuthOperationLockOwnershipStatus.OwnershipLost;

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            RedisResult result = await _database.ScriptEvaluateAsync(
                RenewScript,
                [_key],
                [_owner, (long)_ttl.TotalMilliseconds]);
            cancellationToken.ThrowIfCancellationRequested();
            if ((long)result == 1)
                return GoogleOAuthOperationLockOwnershipStatus.Owned;

            Interlocked.Exchange(ref _ownershipLost, 1);
            return GoogleOAuthOperationLockOwnershipStatus.OwnershipLost;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or InvalidOperationException)
        {
            return GoogleOAuthOperationLockOwnershipStatus.TemporaryFailure;
        }
    }

    private async Task RenewUntilReleasedAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_ttl.TotalMilliseconds / 3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (await EnsureOwnedAsync(cancellationToken) == GoogleOAuthOperationLockOwnershipStatus.OwnershipLost)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _releaseStarted, 1) != 0)
            return;

        _renewalCancellation.Cancel();
        try
        {
            await _renewalTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        try
        {
            await _database.ScriptEvaluateAsync(ReleaseScript, [_key], [_owner]);
        }
        catch (Exception)
        {
            // 釋放失敗時保留 owner key，讓 TTL 自然清除；不可直接刪除，避免誤刪已被其他程序接手的 lease。
        }
        try { _renewalCancellation.Dispose(); } catch (ObjectDisposedException) { }
    }
}
