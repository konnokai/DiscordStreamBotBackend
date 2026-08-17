using DiscordStreamBotBackend.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

/// <summary>
/// 管理後台的 Redis 控制平面橋接；mutation 刻意直接發布且不進入一般通知重送佇列。
/// </summary>
public class AdminSettingsRedisService
{
    // 與既有 Notifier ClusterQueryService 的跨 shard request/reply 預算一致。
    internal static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(2.5);

    private readonly ILogger<AdminSettingsRedisService> _logger;
    private readonly RedisService _redisService;

    public AdminSettingsRedisService(
        ILogger<AdminSettingsRedisService> logger,
        RedisService redisService)
    {
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<HashSet<string>> GetInstalledGuildIdsAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await _redisService.Redis.GetDatabase(0).HashGetAllAsync(RedisChannels.AdminSettings.GuildSnapshotHash);
            cancellationToken.ThrowIfCancellationRequested();

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                try
                {
                    result.UnionWith(ParseGuildSnapshot(entry.Value.ToString()));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "忽略無法解析的 guild snapshot | Shard: {ShardId}", entry.Name.ToString());
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "讀取管理後台 guild snapshot 失敗");
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public Task<string> RequestSnapshotAsync(AdminSettingsRequestEnvelope envelope, CancellationToken cancellationToken)
        => PublishAndWaitAsync(RedisChannels.AdminSettings.SnapshotRequest, envelope, cancellationToken);

    public Task<string> SendCommandAsync(AdminSettingsRequestEnvelope envelope, CancellationToken cancellationToken)
        => PublishAndWaitAsync(RedisChannels.AdminSettings.CommandRequest, envelope, cancellationToken);

    internal static IReadOnlyCollection<string> ParseGuildSnapshot(string json)
    {
        var token = JToken.Parse(json);
        var guilds = token.Type == JTokenType.Array
            ? token.ToObject<List<BotGuildSnapshot>>()
            : token.ToObject<BotGuildSnapshotEnvelope>()?.Guilds;

        return guilds?
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .Select(x => x.Id)
            .ToArray() ?? [];
    }

    /// <summary>先訂閱 correlation reply 再發布，避免 owning shard 的快速回覆在訂閱完成前遺失。</summary>
    private async Task<string> PublishAndWaitAsync(
        string requestChannelName,
        AdminSettingsRequestEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var replyChannel = new RedisChannel(
            RedisChannels.AdminSettings.Reply(envelope.CorrelationId),
            RedisChannel.PatternMode.Literal);
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<RedisChannel, RedisValue> handler = (_, value) => reply.TrySetResult(value.ToString());
        var subscribed = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _redisService.RedisSub.SubscribeAsync(replyChannel, handler);
            subscribed = true;

            cancellationToken.ThrowIfCancellationRequested();
            var subscribers = await _redisService.RedisSub.PublishAsync(
                new RedisChannel(requestChannelName, RedisChannel.PatternMode.Literal),
                JsonConvert.SerializeObject(envelope));
            if (subscribers == 0)
                return null;

            return await reply.Task.WaitAsync(ReplyTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "管理後台 Redis request/reply 失敗 | CorrelationId: {CorrelationId}", envelope.CorrelationId);
            return null;
        }
        finally
        {
            if (subscribed)
            {
                try
                {
                    await _redisService.RedisSub.UnsubscribeAsync(replyChannel, handler);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "取消管理後台 Redis reply 訂閱失敗 | CorrelationId: {CorrelationId}", envelope.CorrelationId);
                }
            }
        }
    }
}
