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

public enum AdminSettingsRedisOutcome
{
    Reply,
    Unavailable,
    DeadlineExceeded
}

public sealed class AdminSettingsRedisResult<T>
{
    private AdminSettingsRedisResult(AdminSettingsRedisOutcome outcome, T value = default)
    {
        Outcome = outcome;
        Value = value;
    }

    public AdminSettingsRedisOutcome Outcome { get; }
    public T Value { get; }

    public static AdminSettingsRedisResult<T> FromReply(T value)
        => new(AdminSettingsRedisOutcome.Reply, value);

    public static AdminSettingsRedisResult<T> Unavailable()
        => new(AdminSettingsRedisOutcome.Unavailable);

    public static AdminSettingsRedisResult<T> DeadlineExceeded()
        => new(AdminSettingsRedisOutcome.DeadlineExceeded);
}

/// <summary>
/// 管理後台的 Redis 控制平面橋接；設定異動直接發布，不進一般通知重送佇列。
/// </summary>
public class AdminSettingsRedisService
{
    private readonly ILogger<AdminSettingsRedisService> _logger;
    private readonly RedisService _redisService;

    public AdminSettingsRedisService(
        ILogger<AdminSettingsRedisService> logger,
        RedisService redisService)
    {
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<AdminSettingsRedisResult<HashSet<string>>> GetInstalledGuildIdsAsync(
        CancellationToken operationCancellationToken,
        CancellationToken deadlineCancellationToken,
        CancellationToken clientCancellationToken)
    {
        try
        {
            operationCancellationToken.ThrowIfCancellationRequested();
            var entries = await _redisService.Redis.GetDatabase(0)
                .HashGetAllAsync(RedisChannels.AdminSettings.GuildSnapshotHash)
                .WaitAsync(operationCancellationToken);
            operationCancellationToken.ThrowIfCancellationRequested();

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                operationCancellationToken.ThrowIfCancellationRequested();
                try
                {
                    result.UnionWith(ParseGuildSnapshot(entry.Value.ToString()));
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "略過無法解析的 guild snapshot | Shard: {ShardId}", entry.Name.ToString());
                }
            }

            return AdminSettingsRedisResult<HashSet<string>>.FromReply(result);
        }
        catch (OperationCanceledException) when (clientCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadlineCancellationToken.IsCancellationRequested)
        {
            return AdminSettingsRedisResult<HashSet<string>>.DeadlineExceeded();
        }
        catch (Exception) when (deadlineCancellationToken.IsCancellationRequested &&
            !clientCancellationToken.IsCancellationRequested)
        {
            return AdminSettingsRedisResult<HashSet<string>>.DeadlineExceeded();
        }
        catch (Exception) when (clientCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "讀取管理後台 guild snapshot 時失敗");
            return AdminSettingsRedisResult<HashSet<string>>.Unavailable();
        }
    }

    public Task<AdminSettingsRedisResult<string>> RequestSnapshotAsync(
        AdminSettingsRequestEnvelope envelope,
        CancellationToken operationCancellationToken,
        CancellationToken deadlineCancellationToken,
        CancellationToken clientCancellationToken)
        => PublishAndWaitAsync(
            RedisChannels.AdminSettings.SnapshotRequest,
            envelope,
            operationCancellationToken,
            deadlineCancellationToken,
            clientCancellationToken);

    public Task<AdminSettingsRedisResult<string>> SendCommandAsync(
        AdminSettingsRequestEnvelope envelope,
        CancellationToken operationCancellationToken,
        CancellationToken deadlineCancellationToken,
        CancellationToken clientCancellationToken)
        => PublishAndWaitAsync(
            RedisChannels.AdminSettings.CommandRequest,
            envelope,
            operationCancellationToken,
            deadlineCancellationToken,
            clientCancellationToken);

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

    /// <summary>先訂閱 correlation reply，再發布 request，避免負責該 shard 的 Notifier 在訂閱完成前就快速回覆，導致回覆遺失。</summary>
    private async Task<AdminSettingsRedisResult<string>> PublishAndWaitAsync(
        string requestChannelName,
        AdminSettingsRequestEnvelope envelope,
        CancellationToken operationCancellationToken,
        CancellationToken deadlineCancellationToken,
        CancellationToken clientCancellationToken)
    {
        var replyChannel = new RedisChannel(
            RedisChannels.AdminSettings.Reply(envelope.CorrelationId),
            RedisChannel.PatternMode.Literal);
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<RedisChannel, RedisValue> handler = (_, value) => reply.TrySetResult(value.ToString());
        var subscribed = false;

        try
        {
            operationCancellationToken.ThrowIfCancellationRequested();
            await _redisService.RedisSub.SubscribeAsync(replyChannel, handler)
                .WaitAsync(operationCancellationToken);
            subscribed = true;

            operationCancellationToken.ThrowIfCancellationRequested();
            var subscribers = await _redisService.RedisSub.PublishAsync(
                new RedisChannel(requestChannelName, RedisChannel.PatternMode.Literal),
                JsonConvert.SerializeObject(envelope))
                .WaitAsync(operationCancellationToken);
            if (subscribers == 0)
                return AdminSettingsRedisResult<string>.Unavailable();

            return AdminSettingsRedisResult<string>.FromReply(
                await reply.Task.WaitAsync(operationCancellationToken));
        }
        catch (OperationCanceledException) when (clientCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadlineCancellationToken.IsCancellationRequested)
        {
            return AdminSettingsRedisResult<string>.DeadlineExceeded();
        }
        catch (Exception) when (deadlineCancellationToken.IsCancellationRequested &&
            !clientCancellationToken.IsCancellationRequested)
        {
            return AdminSettingsRedisResult<string>.DeadlineExceeded();
        }
        catch (Exception) when (clientCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "管理後台 Redis request/reply 發生錯誤 | CorrelationId: {CorrelationId}", envelope.CorrelationId);
            return AdminSettingsRedisResult<string>.Unavailable();
        }
        finally
        {
            if (subscribed)
            {
                try
                {
                    await _redisService.RedisSub.UnsubscribeAsync(replyChannel, handler)
                        .WaitAsync(operationCancellationToken);
                }
                catch (OperationCanceledException) when (operationCancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "取消管理後台 Redis reply 訂閱失敗 | CorrelationId: {CorrelationId}", envelope.CorrelationId);
                }
            }
        }
    }
}
