using System;
using System.Threading.Tasks;
using DiscordStreamBotBackend.Services;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace DiscordStreamBotBackend.YoutubeWebSub;

/// <summary>pending action 與其原始 JSON；原始字串用於 compare-and-set，避免覆蓋 Bot 剛寫入的新狀態。</summary>
public sealed class YoutubeWebSubPendingSnapshot
{
    public YoutubeWebSubPendingAction Action { get; init; }
    public string RawJson { get; init; }
}

/// <summary>
/// Redis DB 1 的 WebSub 共享狀態存取（計畫 §7.1）：pending action 與每頻道固定 HMAC secret。
/// <para>
/// 與 Bot 的 <c>YoutubeWebSubState</c> 使用完全相同的 key 與 JSON 欄位；secret 本身不進 log、
/// 不進 Pub/Sub payload。所有 pending 更新都以原始 JSON 做 compare-and-set，且「更新 secret」與
/// 「標記狀態」放在同一個 transaction，舊的 challenge 不會刪掉新訂閱的 secret。
/// </para>
/// </summary>
public sealed class YoutubeWebSubStateStore
{
    private readonly IDatabase _database;

    public YoutubeWebSubStateStore(IDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>讀取 pending 與原始 JSON；不存在時回傳 null，格式不符時拋出（代表狀態損壞，需人工確認）。</summary>
    public async Task<YoutubeWebSubPendingSnapshot> GetPendingSnapshotAsync(string channelId)
    {
        RedisValue value = await _database.StringGetAsync(RedisChannels.YoutubeWebSub.PendingKey(channelId));
        if (!value.HasValue)
            return null;

        if (!YoutubeWebSubPendingAction.TryParse(value.ToString(), out var action, out var error))
            throw new InvalidOperationException($"YouTube WebSub pending 內容無法解析：{channelId}（{error}）");

        return new YoutubeWebSubPendingSnapshot { Action = action, RawJson = value.ToString() };
    }

    public async Task<YoutubeWebSubPendingAction> GetPendingAsync(string channelId)
        => (await GetPendingSnapshotAsync(channelId))?.Action;

    public async Task<string> GetSecretAsync(string channelId)
    {
        RedisValue value = await _database.StringGetAsync(RedisChannels.YoutubeWebSub.HmacSecretKey(channelId));
        return value.HasValue ? value.ToString() : null;
    }

    /// <summary>把 secret TTL 改成 Hub 實際的 lease；只有 pending 仍是同一筆時才生效。</summary>
    public async Task<bool> TrySetSecretTtlAsync(YoutubeWebSubPendingSnapshot snapshot, TimeSpan ttl)
    {
        var transaction = _database.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(PendingKey(snapshot), snapshot.RawJson));
        Task<bool> expireTask = transaction.KeyExpireAsync(SecretKey(snapshot), ttl);

        if (!await transaction.ExecuteAsync())
            return false;

        // secret 在讀取後消失時 KeyExpire 回 false：此時不能標記訂閱成功。
        return await expireTask;
    }

    /// <summary>
    /// 標記 challenge 已完成；unsubscribe 時在同一個 transaction 內刪除 secret。
    /// 只有 pending 仍是同一筆時才生效，避免舊 challenge 覆蓋新訂閱狀態。
    /// </summary>
    public Task<bool> TryMarkConfirmedAsync(YoutubeWebSubPendingSnapshot snapshot, bool deleteSecret)
    {
        snapshot.Action.ConfirmedAtUtc = DateTime.UtcNow;
        return TryUpdatePendingAsync(snapshot, deleteSecret);
    }

    /// <summary>標記 Hub 已拒絕；不更新 confirmed、LastSubscribeTime 或 secret lease。</summary>
    public Task<bool> TryMarkDeniedAsync(YoutubeWebSubPendingSnapshot snapshot)
    {
        snapshot.Action.DeniedAtUtc = DateTime.UtcNow;
        return TryUpdatePendingAsync(snapshot, deleteSecret: false);
    }

    /// <summary>
    /// 以原始 JSON 做 compare-and-set 寫回 pending；pending 保留到原本的 TTL 到期，
    /// 讓 Hub 在 callback response 遺失時可安全重試同一 challenge。
    /// </summary>
    private async Task<bool> TryUpdatePendingAsync(YoutubeWebSubPendingSnapshot snapshot, bool deleteSecret)
    {
        TimeSpan? remaining = await _database.KeyTimeToLiveAsync(PendingKey(snapshot));
        TimeSpan ttl = remaining ?? TimeSpan.FromDays(10);

        var transaction = _database.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(PendingKey(snapshot), snapshot.RawJson));
        if (deleteSecret)
            _ = transaction.KeyDeleteAsync(SecretKey(snapshot));
        _ = transaction.StringSetAsync(PendingKey(snapshot), JsonConvert.SerializeObject(snapshot.Action), ttl);

        return await transaction.ExecuteAsync();
    }

    private static RedisKey PendingKey(YoutubeWebSubPendingSnapshot snapshot)
        => RedisChannels.YoutubeWebSub.PendingKey(snapshot.Action.ChannelId);

    private static RedisKey SecretKey(YoutubeWebSubPendingSnapshot snapshot)
        => RedisChannels.YoutubeWebSub.HmacSecretKey(snapshot.Action.ChannelId);
}
