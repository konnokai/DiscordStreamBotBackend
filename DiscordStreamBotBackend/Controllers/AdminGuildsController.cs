using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers;

[ApiController]
[Route("admin/guilds")]
[EnableCors("frontend")]
public class AdminGuildsController : ControllerBase
{
    private static readonly TimeSpan SettingsRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly AdminSettingsRedisService _adminSettingsRedisService;
    private readonly BearerTokenService _bearerTokenService;
    private readonly DiscordGuildAuthorizationService _discordGuildAuthorizationService;

    public AdminGuildsController(
        AdminSettingsRedisService adminSettingsRedisService,
        BearerTokenService bearerTokenService,
        DiscordGuildAuthorizationService discordGuildAuthorizationService)
    {
        _adminSettingsRedisService = adminSettingsRedisService;
        _bearerTokenService = bearerTokenService;
        _discordGuildAuthorizationService = discordGuildAuthorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetGuilds(CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(SettingsRequestTimeout);
        using var timeoutCancellation = new CancellationTokenSource(deadline - DateTimeOffset.UtcNow);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        try
        {
            var authorization = await GetAuthorizationAsync(true, requestCancellation.Token);
            if (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Timeout(correlationId);
            if (authorization.Error != null)
                return authorization.Error;

            var installedGuilds = await _adminSettingsRedisService.GetInstalledGuildIdsAsync(
                requestCancellation.Token, timeoutCancellation.Token, cancellationToken);
            if (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Timeout(correlationId);
            if (installedGuilds.Outcome != AdminSettingsRedisOutcome.Reply)
                return RedisFailure(installedGuilds.Outcome, correlationId);

            foreach (var guild in authorization.Guilds)
                guild.BotInstalled = installedGuilds.Value.Contains(guild.Id);

            return Ok(authorization.Guilds);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Timeout(correlationId);
        }
    }

    [HttpGet("{guildId}/settings")]
    public async Task<IActionResult> GetSettings(string guildId, CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(SettingsRequestTimeout);
        using var timeoutCancellation = new CancellationTokenSource(deadline - DateTimeOffset.UtcNow);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        try
        {
            if (!_bearerTokenService.TryGetDiscordSession(Request.Headers.Authorization.ToString(), out var session))
                return Unauthorized(new { error = "invalid_discord_session" });

            var envelope = CreateEnvelope(
                guildId,
                session.DiscordUserId,
                "settings.snapshot",
                new JObject(),
                deadline.ToUnixTimeMilliseconds(),
                correlationId);
            var redisResult = await _adminSettingsRedisService.RequestSnapshotAsync(
                envelope, requestCancellation.Token, timeoutCancellation.Token, cancellationToken);
            if (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Timeout(envelope.CorrelationId);
            if (redisResult.Outcome != AdminSettingsRedisOutcome.Reply)
                return RedisFailure(redisResult.Outcome, envelope.CorrelationId);
            if (!TryReadSnapshotReply(redisResult.Value, out var snapshot))
                return Unavailable(envelope.CorrelationId);

            return Ok(snapshot);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Timeout(correlationId);
        }
    }

    [HttpPost("{guildId}/commands")]
    public async Task<IActionResult> SendCommand(
        string guildId,
        [FromBody] AdminSettingsCommandRequest request,
        CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(SettingsRequestTimeout);
        using var timeoutCancellation = new CancellationTokenSource(deadline - DateTimeOffset.UtcNow);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        try
        {
            var authorization = await GetAuthorizationAsync(false, requestCancellation.Token);
            if (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Timeout(correlationId);
            if (authorization.Error != null)
                return authorization.Error;
            if (request == null || string.IsNullOrWhiteSpace(request.Action) || request.Payload == null)
                return BadRequest(new { error = "invalid_command" });
            if (!authorization.Guilds.Any(x => string.Equals(x.Id, guildId, StringComparison.Ordinal)))
                return StatusCode(403, new { error = "guild_forbidden" });

            var envelope = CreateEnvelope(
                guildId,
                authorization.Session.DiscordUserId,
                request.Action,
                request.Payload,
                deadline.ToUnixTimeMilliseconds(),
                correlationId);
            var redisResult = await _adminSettingsRedisService.SendCommandAsync(
                envelope, requestCancellation.Token, timeoutCancellation.Token, cancellationToken);
            if (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                return Timeout(envelope.CorrelationId);
            if (redisResult.Outcome != AdminSettingsRedisOutcome.Reply)
                return RedisFailure(redisResult.Outcome, envelope.CorrelationId);
            if (!TryReadCommandReply(redisResult.Value, envelope.CorrelationId, out var commandReply))
                return Unavailable(envelope.CorrelationId);

            return Ok(commandReply);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Timeout(correlationId);
        }
    }

    internal static AdminSettingsRequestEnvelope CreateEnvelope(
        string guildId,
        ulong actorUserId,
        string action,
        JObject payload,
        long deadlineUnixMs,
        string correlationId = null)
        => new()
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            GuildId = guildId,
            ActorUserId = actorUserId.ToString(CultureInfo.InvariantCulture),
            DeadlineUnixMs = deadlineUnixMs,
            Action = action,
            Payload = payload
        };

    private async Task<(DiscordSessionPayload Session, List<AdminGuild> Guilds, IActionResult Error)> GetAuthorizationAsync(
        bool useCache,
        CancellationToken cancellationToken)
    {
        if (!_bearerTokenService.TryGetDiscordSession(Request.Headers.Authorization.ToString(), out var session))
            return (null, null, Unauthorized(new { error = "invalid_discord_session" }));

        try
        {
            var guilds = await _discordGuildAuthorizationService.GetManageableGuildsAsync(
                session.DiscordUserId,
                session.DiscordAccessToken,
                session.IssuedAtUtc,
                session.ExpiresAtUtc,
                useCache,
                cancellationToken);
            return (session, guilds, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return (null, null, Unauthorized(new { error = "invalid_discord_session" }));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (null, null, StatusCode(503, new { error = "discord_unavailable" }));
        }
    }

    private IActionResult Unavailable(string correlationId)
        => StatusCode(503, new AdminSettingsCommandReply
        {
            ContractVersion = 1,
            CorrelationId = correlationId,
            State = "unknown",
            Code = "settings.unavailable",
            Arguments = new JObject()
        });

    private IActionResult Timeout(string correlationId)
        => StatusCode(504, new AdminSettingsCommandReply
        {
            ContractVersion = 1,
            CorrelationId = correlationId,
            State = "timeout",
            Code = "settings.timeout",
            Arguments = new JObject()
        });

    private IActionResult RedisFailure(AdminSettingsRedisOutcome outcome, string correlationId)
        => outcome == AdminSettingsRedisOutcome.DeadlineExceeded
            ? Timeout(correlationId)
            : Unavailable(correlationId);

    internal static bool TryReadSnapshotReply(string json, out AdminSettingsSnapshotReply reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            reply = JsonConvert.DeserializeObject<AdminSettingsSnapshotReply>(json);
            return reply?.ContractVersion == 1 && reply.Capabilities != null && reply.Guild != null &&
                reply.Health != null && reply.Resources != null && reply.Common != null && reply.Notifications != null &&
                reply.Crawlers != null && reply.Verification != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool TryReadCommandReply(string json, string correlationId, out AdminSettingsCommandReply reply)
    {
        reply = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            reply = JsonConvert.DeserializeObject<AdminSettingsCommandReply>(json);
            return reply?.ContractVersion == 1 &&
                string.Equals(reply.CorrelationId, correlationId, StringComparison.Ordinal) &&
                reply.ShardId.HasValue &&
                !string.IsNullOrWhiteSpace(reply.State) && !string.IsNullOrWhiteSpace(reply.Code) && reply.Arguments != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
