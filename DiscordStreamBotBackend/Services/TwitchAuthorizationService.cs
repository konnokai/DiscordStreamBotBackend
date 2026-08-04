using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Model.Twitch;
using DiscordStreamBotBackend.Services.Auth;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public enum TwitchUnlinkResult
{
    Unlinked,
    RevocationPending
}

public class TwitchAuthorizationService : IAsyncDisposable
{
    private const string RevocationPendingReason = "revocation_pending";
    private const string UserUnlinkedReason = "user_unlinked";
    private const string RequiredScope = "user:read:subscriptions";
    private const int RefreshedTokenPersistenceAttempts = 6;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IDbContextFactory<MainDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwitchAuthorizationService> _logger;
    private readonly PublicUrlService _publicUrls;
    private readonly TwitchOAuthRefreshLock _refreshLock;
    private readonly TwitchRefreshRotationLifecycle _rotationLifecycle;
    private readonly RedisService _redisService;
    private readonly TokenService _tokenService;
    private readonly ConcurrentDictionary<string, PendingRefreshedToken> _pendingRefreshedTokens = new(StringComparer.Ordinal);
    private readonly object _stopGate = new();
    private Task _stopTask;

    public TwitchAuthorizationService(
        IConfiguration configuration,
        IDbContextFactory<MainDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<TwitchAuthorizationService> logger,
        PublicUrlService publicUrls,
        RedisService redisService,
        TokenService tokenService)
    {
        _clientId = configuration["Twitch:ClientId"];
        _clientSecret = configuration["Twitch:ClientSecret"];
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _publicUrls = publicUrls;
        _redisService = redisService;
        _refreshLock = new TwitchOAuthRefreshLock(redisService.RedisDb);
        _rotationLifecycle = new TwitchRefreshRotationLifecycle(
            count => BackendMetrics.TwitchRefreshPendingPersistence.Set(count));
        _tokenService = tokenService;
    }

    public string GetAuthorizationUrl(string state)
    {
        return QueryHelpers.AddQueryString("https://id.twitch.tv/oauth2/authorize", new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = _publicUrls.TwitchCallbackUrl,
            ["response_type"] = "code",
            ["scope"] = RequiredScope,
            ["state"] = state,
            ["force_verify"] = "true"
        });
    }

    /// <summary>完成 Twitch OAuth callback，在共用 lease 內驗證身分與 scope，並以條件式寫入保存授權。</summary>
    public async Task<string> CompleteAuthorizationAsync(ulong discordUserId, string code, CancellationToken cancellationToken)
    {
        var tokenResult = await ExchangeCodeAsync(code, cancellationToken);
        if (tokenResult.Status != TwitchApiResultStatus.Success || string.IsNullOrWhiteSpace(tokenResult.Value?.AccessToken) || string.IsNullOrWhiteSpace(tokenResult.Value.RefreshToken))
            return "token_exchange_failed";
        var token = tokenResult.Value;

        var validationResult = await ValidateTokenAsync(token.AccessToken, cancellationToken);
        if (validationResult.Status != TwitchApiResultStatus.Success)
            return "provider_validation_failed";
        var validation = validationResult.Value;
        if (validation.ClientId != _clientId || string.IsNullOrWhiteSpace(validation.UserId))
            return "provider_validation_failed";
        if (validation.Scopes == null || !validation.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
            return "provider_validation_failed";

        token = NormalizeTokenForPersistence(token, validation, token.RefreshToken, token.TokenType);
        if (!IsUsableTokenForBot(token, validation.UserId))
            return "provider_validation_failed";

        var profile = await GetUserProfileAsync(token.AccessToken, cancellationToken);
        if (profile == null || profile.Id != validation.UserId)
            return "provider_validation_failed";

        var lockResult = await TryAcquireRefreshLockAsync(validation.UserId, "authorization_callback", cancellationToken);
        if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
            return "authorization_busy";

        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var byDiscord = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
            var byTwitch = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.TwitchUserId == validation.UserId, cancellationToken);

            if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, validation.UserId, "authorization_callback", cancellationToken))
                return "authorization_busy";

            if (byDiscord != null && byDiscord.TwitchUserId != validation.UserId)
            {
                if (byDiscord.RevocationReason != UserUnlinkedReason)
                    return "account_conflict";
                db.TwitchBroadcasterAuthorization.Remove(byDiscord);
                byDiscord = null;
            }

            if (byTwitch != null && byTwitch.DiscordUserId != discordUserId)
            {
                if (byTwitch.RevocationReason != UserUnlinkedReason)
                    return "account_conflict";
                db.TwitchBroadcasterAuthorization.Remove(byTwitch);
                byTwitch = null;
            }

            if (db.ChangeTracker.HasChanges())
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, validation.UserId, "authorization_callback_cleanup", cancellationToken))
                    return "authorization_busy";
                await db.SaveChangesAsync(cancellationToken);
            }

            var now = UtcNowForMySql();
            var entity = byTwitch ?? byDiscord;
            if (entity == null)
            {
                entity = new TwitchBroadcasterAuthorization { TwitchUserId = validation.UserId };
                db.TwitchBroadcasterAuthorization.Add(entity);
            }

            entity.DiscordUserId = discordUserId;
            entity.ClientId = validation.ClientId;
            entity.UserLogin = profile.Login;
            entity.DisplayName = profile.DisplayName;
            entity.ProfileImageUrl = profile.ProfileImageUrl;
            entity.EncryptedAccessToken = _tokenService.CreateTokenResponseToken(token);
            entity.Scopes = JsonConvert.SerializeObject(validation.Scopes);
            entity.TokenExpiresAt = now.AddSeconds(validation.ExpiresIn);
            entity.LastValidatedAt = now;
            entity.AuthorizedAt = now;
            entity.RevokedAt = null;
            entity.RevocationReason = null;
            entity.DateUpdated = now;

            try
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, validation.UserId, "authorization_callback_persistence", cancellationToken))
                    return "authorization_busy";
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "Twitch OAuth 帳號衝突");
                return "account_conflict";
            }

            await TryPublishAuthorizationChangedAsync(entity, "linked", CancellationToken.None);
            await TryUpdateMetricsAsync(CancellationToken.None);
            return null;
        }
        finally
        {
            await ReleaseRefreshLockAsync(lockResult.Lease, validation.UserId, "authorization_callback", CancellationToken.None);
        }
    }

    public async Task<TwitchAccountLink> GetAccountLinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var entity = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        if (entity == null)
            return new TwitchAccountLink { Status = "unlinked" };

        var status = entity.RevokedAt == null
            ? entity.ClientId == _clientId && !string.IsNullOrWhiteSpace(entity.EncryptedAccessToken) ? "linked" : "invalid"
            : entity.RevocationReason == UserUnlinkedReason ? "revoked" : "invalid";
        return new TwitchAccountLink
        {
            Status = status,
            TwitchUserId = entity.TwitchUserId,
            UserLogin = entity.UserLogin,
            DisplayName = entity.DisplayName,
            ProfileImageUrl = entity.ProfileImageUrl
        };
    }

    /// <summary>先將 unlink 意圖保存至 MySQL，再於 refresh lease 內撤銷 provider token 並完成本地失效狀態。</summary>
    public async Task<TwitchUnlinkResult> UnlinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var db = _dbContextFactory.CreateDbContext();
        var entity = await db.TwitchBroadcasterAuthorization
            .SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        if (entity == null || entity.RevocationReason == UserUnlinkedReason)
            return TwitchUnlinkResult.Unlinked;

        if (entity.RevocationReason != RevocationPendingReason)
        {
            await MarkRevocationPendingAsync(entity, db, cancellationToken);
            await TryPublishAuthorizationChangedAsync(entity, "invalid", cancellationToken);
        }

        var twitchUserId = entity.TwitchUserId;
        var lockResult = await TryAcquireRefreshLockAsync(twitchUserId, "unlink", cancellationToken);
        if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
            return TwitchUnlinkResult.RevocationPending;

        try
        {
            if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "unlink", cancellationToken))
                return TwitchUnlinkResult.RevocationPending;

            using var lockedDb = _dbContextFactory.CreateDbContext();
            entity = await lockedDb.TwitchBroadcasterAuthorization
                .SingleOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);
            if (entity == null || entity.DiscordUserId != discordUserId)
                return TwitchUnlinkResult.Unlinked;
            if (entity.RevocationReason == UserUnlinkedReason)
                return TwitchUnlinkResult.Unlinked;
            if (entity.RevocationReason != RevocationPendingReason)
                return TwitchUnlinkResult.RevocationPending;

            var revokeStatus = CanFinalizeRevocationWithoutProviderToken(entity.EncryptedAccessToken)
                ? TwitchApiResultStatus.Invalid
                : TwitchApiResultStatus.TransientFailure;
            if (!CanFinalizeRevocationWithoutProviderToken(entity.EncryptedAccessToken))
            {
                try
                {
                    var token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(entity.EncryptedAccessToken);
                    if (!string.IsNullOrWhiteSpace(token?.AccessToken))
                        revokeStatus = await RevokeTokenAsync(token.AccessToken, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Twitch token 解密失敗，保留 token 並標記待重試撤銷");
                }
            }

            if (revokeStatus == TwitchApiResultStatus.Success || revokeStatus == TwitchApiResultStatus.Invalid)
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "unlink", cancellationToken))
                    return TwitchUnlinkResult.RevocationPending;

                await FinalizeUnlinkAsync(entity, lockedDb, cancellationToken);
                await TryUpdateMetricsAsync(CancellationToken.None);
                return TwitchUnlinkResult.Unlinked;
            }

            await TryUpdateMetricsAsync(CancellationToken.None);
            return TwitchUnlinkResult.RevocationPending;
        }
        finally
        {
            await ReleaseRefreshLockAsync(lockResult.Lease, twitchUserId, "unlink", CancellationToken.None);
        }
    }

    /// <summary>重試待處理撤銷、驗證所有有效授權、補送失效事件並更新 OAuth 指標。</summary>
    public async Task ValidateAllAsync(CancellationToken cancellationToken)
    {
        await RetryPendingRevocationsAsync(cancellationToken);

        using var db = _dbContextFactory.CreateDbContext();
        var userIds = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .Where(x => x.RevokedAt == null)
            .Select(x => x.TwitchUserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            if (_pendingRefreshedTokens.ContainsKey(userId))
            {
                BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "refresh_persistence_pending").Inc();
                continue;
            }

            try
            {
                await ValidateStoredAuthorizationAsync(userId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "error").Inc();
                _logger.LogError(ex, "Twitch token 定期驗證發生暫時性錯誤");
            }
        }

        await ReplayInvalidatedAuthorizationsAsync(cancellationToken);
        await UpdateMetricsAsync(cancellationToken);
    }

    /// <summary>以 Twitch validation 正規化 token 的身分、scope、期限與型別，產生 Bot 可讀的共用契約。</summary>
    internal static TwitchAccessTokenData NormalizeTokenForPersistence(
        TwitchAccessTokenData token,
        TwitchValidateTokenData validation,
        string fallbackRefreshToken,
        string fallbackTokenType)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(validation);

        // Provider validation 才是授權真相；身分、scope、期限與 token type 一律正規化後再加密。
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            token.RefreshToken = fallbackRefreshToken;
        token.TwitchUserId = validation.UserId;
        token.Scopes = (validation.Scopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        token.TokenType = NormalizeTokenType(token.TokenType, fallbackTokenType);
        token.ExpiresIn = Math.Max(0, validation.ExpiresIn);
        return token;
    }

    internal static bool IsUsableTokenForBot(TwitchAccessTokenData token, string twitchUserId)
        => token != null &&
            !string.IsNullOrWhiteSpace(token.AccessToken) &&
            !string.IsNullOrWhiteSpace(token.RefreshToken) &&
            string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(token.TwitchUserId) &&
            token.TwitchUserId == twitchUserId &&
            token.Scopes?.Contains(RequiredScope, StringComparer.Ordinal) == true;

    private static string NormalizeTokenType(string tokenType, string fallbackTokenType)
    {
        var value = string.IsNullOrWhiteSpace(tokenType) ? fallbackTokenType : tokenType;
        return string.IsNullOrWhiteSpace(value) ? "bearer" : value.Trim().ToLowerInvariant();
    }

    /// <summary>在 refresh lease 內重讀、驗證並視需要 rotation MySQL 授權，暫時錯誤不撤銷資料。</summary>
    private async Task ValidateStoredAuthorizationAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        var lockResult = await TryAcquireRefreshLockAsync(twitchUserId, "stored_token_validation", cancellationToken);
        if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
        {
            BackendMetrics.OAuthTokenValidations
                .WithLabels("twitch", lockResult.Status == TwitchOAuthRefreshLockAcquireStatus.Contended ? "lock_contended" : "lock_temporary_failure")
                .Inc();
            return;
        }

        try
        {
            using var db = _dbContextFactory.CreateDbContext();
            var entity = await db.TwitchBroadcasterAuthorization
                .SingleOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);
            if (entity == null || entity.RevokedAt != null)
                return;

            TwitchAccessTokenData token;
            try
            {
                token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(entity.EncryptedAccessToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twitch token 解密失敗");
                BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "decrypt_failure").Inc();
                return;
            }

            var validationResult = await ValidateTokenAsync(token?.AccessToken, cancellationToken);
            var refreshed = false;
            if (validationResult.Status == TwitchApiResultStatus.TransientFailure || validationResult.Status == TwitchApiResultStatus.Failure)
            {
                BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "temporary_failure").Inc();
                return;
            }

            if (validationResult.Status == TwitchApiResultStatus.Invalid)
            {
                if (!_rotationLifecycle.TryBeginRefresh(out var refreshOperation))
                {
                    BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "shutdown_rejected").Inc();
                    return;
                }

                using (refreshOperation)
                {
                    // provider 接受 rotation 後使用不可取消的保存路徑，並在 operation 結束前交給 drain 追蹤。
                    var refreshResult = await RefreshTokenAsync(token?.RefreshToken, CancellationToken.None);
                    if (refreshResult.Status == TwitchApiResultStatus.Invalid)
                    {
                        BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "failure").Inc();
                        if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                            return;
                        await MarkRevokedAsync(entity, "refresh_invalid", db, cancellationToken);
                        return;
                    }
                    if (refreshResult.Status != TwitchApiResultStatus.Success)
                    {
                        BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "temporary_failure").Inc();
                        return;
                    }

                    var refreshedToken = refreshResult.Value;
                    if (string.IsNullOrWhiteSpace(refreshedToken.RefreshToken))
                        refreshedToken.RefreshToken = token.RefreshToken;
                    refreshedToken.TwitchUserId = entity.TwitchUserId;
                    refreshedToken.Scopes ??= token.Scopes;
                    refreshedToken.TokenType = NormalizeTokenType(refreshedToken.TokenType, token.TokenType);
                    token = refreshedToken;

                    await SaveRefreshedTokenForRetryAsync(
                        entity.TwitchUserId,
                        entity.EncryptedAccessToken,
                        token,
                        lockResult.Lease,
                        CancellationToken.None);
                    await db.Entry(entity).ReloadAsync(cancellationToken);
                    if (entity.RevokedAt != null)
                        return;

                    validationResult = await ValidateTokenAsync(token.AccessToken, cancellationToken);
                    refreshed = true;
                    BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "success").Inc();

                    if (validationResult.Status == TwitchApiResultStatus.TransientFailure || validationResult.Status == TwitchApiResultStatus.Failure)
                    {
                        BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "temporary_failure").Inc();
                        return;
                    }
                }
            }

            if (validationResult.Status == TwitchApiResultStatus.Invalid)
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                    return;
                await MarkRevokedAsync(entity, "token_invalid", db, cancellationToken);
                return;
            }
            var validation = validationResult.Value;
            if (validation.ClientId != _clientId)
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                    return;
                await MarkRevokedAsync(entity, "client_id_mismatch", db, cancellationToken);
                return;
            }
            if (validation.UserId != entity.TwitchUserId)
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                    return;
                await MarkRevokedAsync(entity, "user_id_mismatch", db, cancellationToken);
                return;
            }
            if (validation.Scopes == null || !validation.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
            {
                if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                    return;
                await MarkRevokedAsync(entity, "scope_mismatch", db, cancellationToken);
                return;
            }

            if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, twitchUserId, "stored_token_validation", cancellationToken))
                return;

            var now = UtcNowForMySql();
            entity.UserLogin = validation.Login ?? entity.UserLogin;
            entity.Scopes = JsonConvert.SerializeObject(validation.Scopes ?? []);
            entity.TokenExpiresAt = now.AddSeconds(validation.ExpiresIn);
            entity.LastValidatedAt = now;
            entity.DateUpdated = now;
            await db.SaveChangesAsync(cancellationToken);
            BackendMetrics.OAuthTokenValidations.WithLabels("twitch", refreshed ? "refreshed" : "valid").Inc();
        }
        finally
        {
            if (!_pendingRefreshedTokens.TryGetValue(twitchUserId, out var pending) ||
                !ReferenceEquals(pending.Lease, lockResult.Lease))
            {
                await ReleaseRefreshLockAsync(lockResult.Lease, twitchUserId, "stored_token_validation", CancellationToken.None);
            }
            else
            {
                _logger.LogWarning(
                    "Twitch rotation 後 token 尚未寫入 MySQL，持續續租 refresh lock 並由背景工作重試 | TwitchUserId: {TwitchUserId}",
                    twitchUserId);
            }
        }
    }

    private async Task MarkRevokedAsync(TwitchBroadcasterAuthorization entity, string reason, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = UtcNowForMySql();
        var affected = await db.TwitchBroadcasterAuthorization
            .Where(x =>
                x.TwitchUserId == entity.TwitchUserId &&
                x.RevokedAt == null &&
                x.DateUpdated == entity.DateUpdated &&
                x.EncryptedAccessToken == entity.EncryptedAccessToken)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.EncryptedAccessToken, (string)null)
                .SetProperty(x => x.TokenExpiresAt, (DateTime?)null)
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevocationReason, reason)
                .SetProperty(x => x.DateUpdated, now), cancellationToken);
        ThrowIfStateChanged(affected, entity.TwitchUserId, "authorization invalidation");

        entity.EncryptedAccessToken = null;
        entity.TokenExpiresAt = null;
        entity.RevokedAt = now;
        entity.RevocationReason = reason;
        entity.DateUpdated = now;
        BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "revoked").Inc();
        await TryPublishAuthorizationChangedAsync(entity, "invalid", cancellationToken);
    }

    private async Task<TwitchApiResult<TwitchAccessTokenData>> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        return await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = _publicUrls.TwitchCallbackUrl
        }, cancellationToken);
    }

    private async Task<TwitchApiResult<TwitchAccessTokenData>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return TwitchApiResult<TwitchAccessTokenData>.Failure();

        return await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, cancellationToken);
    }

    private async Task<TwitchApiResult<TwitchAccessTokenData>> PostTokenAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClientFactory.CreateClient().PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(values),
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var token = JsonConvert.DeserializeObject<TwitchAccessTokenData>(body);
                return string.IsNullOrWhiteSpace(token?.AccessToken)
                    ? TwitchApiResult<TwitchAccessTokenData>.Failure()
                    : TwitchApiResult<TwitchAccessTokenData>.Success(token);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return TwitchApiResult<TwitchAccessTokenData>.Invalid();
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return TwitchApiResult<TwitchAccessTokenData>.TransientFailure();

            TwitchTokenErrorData error = null;
            try { error = JsonConvert.DeserializeObject<TwitchTokenErrorData>(body); } catch (JsonException) { }
            if (string.Equals(error?.Error, "invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("invalid refresh token", StringComparison.OrdinalIgnoreCase))
                return TwitchApiResult<TwitchAccessTokenData>.Invalid();
            return TwitchApiResult<TwitchAccessTokenData>.Failure();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twitch token API 暫時無法連線");
            return TwitchApiResult<TwitchAccessTokenData>.TransientFailure();
        }
    }

    private async Task<TwitchApiResult<TwitchValidateTokenData>> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return TwitchApiResult<TwitchValidateTokenData>.Failure();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return TwitchApiResult<TwitchValidateTokenData>.Invalid();
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return TwitchApiResult<TwitchValidateTokenData>.TransientFailure();
            if (!response.IsSuccessStatusCode)
                return TwitchApiResult<TwitchValidateTokenData>.Failure();

            var validation = JsonConvert.DeserializeObject<TwitchValidateTokenData>(await response.Content.ReadAsStringAsync(cancellationToken));
            return validation == null
                ? TwitchApiResult<TwitchValidateTokenData>.Failure()
                : TwitchApiResult<TwitchValidateTokenData>.Success(validation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twitch token 驗證 API 暫時無法連線");
            return TwitchApiResult<TwitchValidateTokenData>.TransientFailure();
        }
    }

    private async Task<TwitchUserData> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.twitch.tv/helix/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Id", _clientId);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var data = JsonConvert.DeserializeObject<TwitchUsersResponse>(await response.Content.ReadAsStringAsync(cancellationToken));
        return data?.Data?.SingleOrDefault();
    }

    private async Task<TwitchApiResultStatus> RevokeTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClientFactory.CreateClient().PostAsync(
                "https://id.twitch.tv/oauth2/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["token"] = accessToken
                }),
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return TwitchApiResultStatus.Success;
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return TwitchApiResultStatus.Invalid;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return TwitchApiResultStatus.TransientFailure;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest &&
                body.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
                body.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                return TwitchApiResultStatus.Invalid;
            }

            _logger.LogWarning("Twitch provider 撤銷回傳非成功狀態碼: {StatusCode}", (int)response.StatusCode);
            return TwitchApiResultStatus.Failure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twitch provider 撤銷暫時失敗");
            return TwitchApiResultStatus.TransientFailure;
        }
    }

    /// <summary>重試已持久化為 revocation_pending 的授權，直到 provider 撤銷與本地 finalization 完成。</summary>
    private async Task RetryPendingRevocationsAsync(CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var userIds = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .Where(x => x.RevocationReason == RevocationPendingReason)
            .Select(x => x.TwitchUserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            TwitchOAuthRefreshLockAcquireResult lockResult = null;
            try
            {
                lockResult = await TryAcquireRefreshLockAsync(userId, "pending_revocation", cancellationToken);
                if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
                    continue;

                using var retryDb = _dbContextFactory.CreateDbContext();
                var entity = await retryDb.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.TwitchUserId == userId, cancellationToken);
                if (entity?.RevocationReason != RevocationPendingReason)
                    continue;

                var result = TwitchApiResultStatus.Invalid;
                if (!CanFinalizeRevocationWithoutProviderToken(entity.EncryptedAccessToken))
                {
                    var token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(entity.EncryptedAccessToken);
                    if (!string.IsNullOrWhiteSpace(token?.AccessToken))
                        result = await RevokeTokenAsync(token.AccessToken, cancellationToken);
                }

                if (result == TwitchApiResultStatus.Success || result == TwitchApiResultStatus.Invalid)
                {
                    if (!await EnsureRefreshLockOwnedAsync(lockResult.Lease, userId, "pending_revocation", cancellationToken))
                        continue;
                    await FinalizeUnlinkAsync(entity, retryDb, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twitch pending revoke 重試失敗，使用者: {TwitchUserId}", userId);
            }
            finally
            {
                if (lockResult?.Status == TwitchOAuthRefreshLockAcquireStatus.Acquired)
                    await ReleaseRefreshLockAsync(lockResult.Lease, userId, "pending_revocation", CancellationToken.None);
            }
        }
    }

    private async Task MarkRevocationPendingAsync(TwitchBroadcasterAuthorization entity, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = UtcNowForMySql();
        var affected = await db.TwitchBroadcasterAuthorization
            .Where(x =>
                x.TwitchUserId == entity.TwitchUserId &&
                x.DiscordUserId == entity.DiscordUserId &&
                x.DateUpdated == entity.DateUpdated &&
                x.RevocationReason == entity.RevocationReason &&
                x.EncryptedAccessToken == entity.EncryptedAccessToken)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevocationReason, RevocationPendingReason)
                .SetProperty(x => x.DateUpdated, now), cancellationToken);
        ThrowIfStateChanged(affected, entity.TwitchUserId, "revocation pending intent");

        entity.RevokedAt = now;
        entity.RevocationReason = RevocationPendingReason;
        entity.DateUpdated = now;
    }

    internal static bool CanFinalizeRevocationWithoutProviderToken(string encryptedAccessToken)
        => string.IsNullOrWhiteSpace(encryptedAccessToken);

    /// <summary>以 token 密文與更新時間 CAS 將 unlink 完成狀態寫入 MySQL，並發布可重播的失效事件。</summary>
    private async Task FinalizeUnlinkAsync(TwitchBroadcasterAuthorization entity, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = UtcNowForMySql();
        var affected = await db.TwitchBroadcasterAuthorization
            .Where(x =>
                x.TwitchUserId == entity.TwitchUserId &&
                x.DiscordUserId == entity.DiscordUserId &&
                x.RevocationReason == RevocationPendingReason &&
                x.DateUpdated == entity.DateUpdated &&
                x.EncryptedAccessToken == entity.EncryptedAccessToken)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.EncryptedAccessToken, (string)null)
                .SetProperty(x => x.TokenExpiresAt, (DateTime?)null)
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevocationReason, UserUnlinkedReason)
                .SetProperty(x => x.DateUpdated, now), cancellationToken);
        ThrowIfStateChanged(affected, entity.TwitchUserId, "unlink finalization");

        entity.EncryptedAccessToken = null;
        entity.TokenExpiresAt = null;
        entity.RevokedAt = now;
        entity.RevocationReason = UserUnlinkedReason;
        entity.DateUpdated = now;
        await TryPublishAuthorizationChangedAsync(entity, "invalid", cancellationToken);
    }

    /// <summary>登記 provider 已接受的 rotation，立即嘗試 MySQL CAS，失敗則連同 lease 移交持續保存。</summary>
    private async Task SaveRefreshedTokenForRetryAsync(
        string twitchUserId,
        string expectedEncryptedToken,
        TwitchAccessTokenData token,
        TwitchOAuthRefreshLockLease lease,
        CancellationToken cancellationToken)
    {
        // Provider 接受 rotation 後先登記記憶體交接，再嘗試 MySQL CAS。
        // 立即保存失敗時，背景工作與 shutdown drain 會持續保護唯一有效的 replacement。
        var now = UtcNowForMySql();
        var pending = new PendingRefreshedToken(
            expectedEncryptedToken,
            _tokenService.CreateTokenResponseToken(token),
            token.ExpiresIn > 0 ? now.AddSeconds(token.ExpiresIn) : null,
            lease);
        await RegisterPendingRefreshedTokenAsync(twitchUserId, pending);

        if (await PersistRefreshedTokenWithRetryAsync(twitchUserId, pending, lease, cancellationToken))
            return;

        if (_pendingRefreshedTokens.TryGetValue(twitchUserId, out var current) &&
            ReferenceEquals(current, pending))
        {
            QueuePendingRefreshPersistence(twitchUserId, pending);
        }

        throw new InvalidOperationException("Twitch refresh token 已 rotation，已保留 refresh lock 並排入持續保存。");
    }

    /// <summary>觸發目前記憶體中所有已接受 rotation 的保存重試，不會丟棄尚未落盤的 replacement。</summary>
    public async Task RetryPendingRefreshPersistenceAsync(CancellationToken cancellationToken)
    {
        foreach (var item in _pendingRefreshedTokens.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RetryPendingRefreshPersistenceItemAsync(item.Key, item.Value, cancellationToken);
        }
    }

    /// <summary>將 pending rotation 登記到背景重試與 shutdown drain，且每筆 rotation 只排入一次。</summary>
    private void QueuePendingRefreshPersistence(string twitchUserId, PendingRefreshedToken pending)
    {
        if (!pending.TryMarkRetryQueued())
            return;

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = RunQueuedPendingRefreshPersistenceAsync(twitchUserId, pending, start.Task);
        _rotationLifecycle.TrackAcceptedPersistence(task);
        start.SetResult();
    }

    private async Task RunQueuedPendingRefreshPersistenceAsync(
        string twitchUserId,
        PendingRefreshedToken pending,
        Task start)
    {
        await start;
        try
        {
            while (_pendingRefreshedTokens.TryGetValue(twitchUserId, out var current) &&
                ReferenceEquals(current, pending))
            {
                await RetryPendingRefreshPersistenceItemAsync(twitchUserId, pending, CancellationToken.None);
                if (!_pendingRefreshedTokens.TryGetValue(twitchUserId, out current) ||
                    !ReferenceEquals(current, pending))
                {
                    break;
                }

                _logger.LogWarning(
                    "仍在等待 Twitch rotation token 保存完成，持續持有並續租 refresh lock | TwitchUserId: {TwitchUserId}",
                    twitchUserId);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
        finally
        {
            await ReleaseRefreshLockAsync(
                pending.Lease,
                twitchUserId,
                "pending_refresh_persistence_completed",
                CancellationToken.None);
        }
    }

    /// <summary>序列化單筆 rotation 的重試，必要時重新取得過期 lease，再執行條件式保存。</summary>
    private async Task RetryPendingRefreshPersistenceItemAsync(
        string twitchUserId,
        PendingRefreshedToken pending,
        CancellationToken cancellationToken)
    {
        await pending.PersistenceGate.WaitAsync(cancellationToken);
        try
        {
            if (!_pendingRefreshedTokens.TryGetValue(twitchUserId, out var current) ||
                !ReferenceEquals(current, pending))
            {
                return;
            }

            var ownership = await pending.Lease.EnsureOwnedAsync(cancellationToken);
            if (ownership.Status == TwitchOAuthRefreshLockOwnershipStatus.OwnershipLost)
            {
                var expiredLease = pending.Lease;
                var lockResult = await TryAcquireRefreshLockAsync(
                    twitchUserId,
                    "pending_refresh_persistence",
                    cancellationToken);
                if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
                    return;

                if (!_pendingRefreshedTokens.TryGetValue(twitchUserId, out current) ||
                    !ReferenceEquals(current, pending))
                {
                    await ReleaseRefreshLockAsync(
                        lockResult.Lease,
                        twitchUserId,
                        "pending_refresh_persistence_stale_entry",
                        CancellationToken.None);
                    return;
                }

                pending.Lease = lockResult.Lease;
                await ReleaseRefreshLockAsync(
                    expiredLease,
                    twitchUserId,
                    "pending_refresh_persistence_expired_lease",
                    CancellationToken.None);
            }
            else if (ownership.Status == TwitchOAuthRefreshLockOwnershipStatus.TemporaryFailure)
            {
                _logger.LogWarning(
                    ownership.Exception,
                    "Twitch rotation 後 token 重試時無法確認 refresh lock owner，保留待下次重試 | TwitchUserId: {TwitchUserId}",
                    twitchUserId);
                return;
            }

            await PersistRefreshedTokenWithRetryAsync(
                twitchUserId,
                pending,
                pending.Lease,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Twitch rotation 後 token 仍無法寫入 MySQL，保留在記憶體待下次重試 | TwitchUserId: {TwitchUserId}",
                twitchUserId);
        }
        finally
        {
            pending.PersistenceGate.Release();
        }
    }

    /// <summary>在 lease owner 保護下以舊密文 CAS 保存 replacement，並辨識冪等完成或 stale 狀態。</summary>
    private async Task<bool> PersistRefreshedTokenWithRetryAsync(
        string twitchUserId,
        PendingRefreshedToken pending,
        TwitchOAuthRefreshLockLease lease,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= RefreshedTokenPersistenceAttempts; attempt++)
        {
            if (!await EnsureRefreshLockOwnedAsync(lease, twitchUserId, "refresh_token_persistence", cancellationToken))
                return false;

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var persistedAt = UtcNowForMySql();
                // 只有仍保存 refresh 前密文的 row 可接收 replacement；0 列代表 revoke、relink 或其他 rotation 已先更新。
                var affected = await db.TwitchBroadcasterAuthorization
                    .Where(x =>
                        x.TwitchUserId == twitchUserId &&
                        (x.RevocationReason == null || x.RevocationReason == RevocationPendingReason) &&
                        x.EncryptedAccessToken == pending.ExpectedEncryptedToken)
                    .ExecuteUpdateAsync(updates => updates
                        .SetProperty(x => x.EncryptedAccessToken, pending.EncryptedToken)
                        .SetProperty(x => x.TokenExpiresAt, pending.TokenExpiresAt)
                        .SetProperty(x => x.DateUpdated, persistedAt), cancellationToken);
                if (affected != 1)
                {
                    var currentState = await db.TwitchBroadcasterAuthorization.AsNoTracking()
                        .Where(x => x.TwitchUserId == twitchUserId)
                        .Select(x => new { x.EncryptedAccessToken, x.RevocationReason })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (currentState != null && IsRefreshedTokenPersistenceSatisfied(
                        currentState.EncryptedAccessToken,
                        currentState.RevocationReason,
                        pending.EncryptedToken))
                    {
                        TryRemovePendingRefreshedToken(twitchUserId, pending);
                        return true;
                    }

                    TryRemovePendingRefreshedToken(twitchUserId, pending);
                    _logger.LogWarning(
                        "Twitch rotation 後 token 寫入時授權 row 已撤銷或不存在，未覆寫較新的狀態 | TwitchUserId: {TwitchUserId}",
                        twitchUserId);
                    return false;
                }

                TryRemovePendingRefreshedToken(twitchUserId, pending);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < RefreshedTokenPersistenceAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1));
                _logger.LogWarning(
                    ex,
                    "Twitch rotation 後 token 寫入 MySQL 暫時失敗，準備重試 | Attempt: {Attempt}/{MaxAttempts} | TwitchUserId: {TwitchUserId}",
                    attempt,
                    RefreshedTokenPersistenceAttempts,
                    twitchUserId);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Twitch rotation 後 token 寫入 MySQL 已用完立即重試次數，轉交持續保存工作 | Attempt: {Attempt}/{MaxAttempts} | TwitchUserId: {TwitchUserId}",
                    attempt,
                    RefreshedTokenPersistenceAttempts,
                    twitchUserId);
                return false;
            }
        }

        return false;
    }

    /// <summary>從 MySQL revoked 狀態補送可能遺失的授權事件，發布前仍會檢查目前版本。</summary>
    private async Task ReplayInvalidatedAuthorizationsAsync(CancellationToken cancellationToken)
    {
        // Redis Pub/Sub 不保證服務離線或發布失敗時送達，因此從 MySQL 的 revoked 狀態定期補送。
        // 真正發布前仍會比對目前版本，避免舊 invalid 事件影響已重新連結的帳號。
        using var db = _dbContextFactory.CreateDbContext();
        var candidates = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .Where(x => x.RevokedAt != null)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            TwitchOAuthRefreshLockAcquireResult lockResult = null;
            try
            {
                lockResult = await TryAcquireRefreshLockAsync(candidate.TwitchUserId, "authorization_invalidation_replay", cancellationToken);
                if (lockResult.Status != TwitchOAuthRefreshLockAcquireStatus.Acquired)
                    continue;

                if (await EnsureRefreshLockOwnedAsync(
                    lockResult.Lease,
                    candidate.TwitchUserId,
                    "authorization_invalidation_replay",
                    cancellationToken))
                {
                    await TryPublishAuthorizationChangedAsync(candidate, "invalid", cancellationToken);
                }
            }
            finally
            {
                if (lockResult?.Status == TwitchOAuthRefreshLockAcquireStatus.Acquired)
                {
                    await ReleaseRefreshLockAsync(
                        lockResult.Lease,
                        candidate.TwitchUserId,
                        "authorization_invalidation_replay",
                        CancellationToken.None);
                }
            }
        }
    }

    private async Task<TwitchOAuthRefreshLockAcquireResult> TryAcquireRefreshLockAsync(
        string twitchUserId,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await _refreshLock.TryAcquireAsync(twitchUserId, cancellationToken);
        if (result.Status == TwitchOAuthRefreshLockAcquireStatus.Contended)
        {
            _logger.LogInformation(
                "Twitch OAuth refresh lock 已由其他實例持有，延後處理 | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }
        else if (result.Status == TwitchOAuthRefreshLockAcquireStatus.TemporaryFailure)
        {
            _logger.LogWarning(
                result.Exception,
                "Twitch OAuth refresh lock 暫時無法取得，保留現有授權狀態 | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }

        return result;
    }

    private async Task<bool> EnsureRefreshLockOwnedAsync(
        TwitchOAuthRefreshLockLease lease,
        string twitchUserId,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await lease.EnsureOwnedAsync(cancellationToken);
        if (result.Status == TwitchOAuthRefreshLockOwnershipStatus.Owned)
            return true;

        if (result.Status == TwitchOAuthRefreshLockOwnershipStatus.OwnershipLost)
        {
            _logger.LogWarning(
                "Twitch OAuth refresh lock owner 已變更，停止寫入以避免 stale holder 覆寫 | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }
        else
        {
            _logger.LogWarning(
                result.Exception,
                "Twitch OAuth refresh lock 無法確認 owner，停止寫入以避免 stale holder 覆寫 | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }

        return false;
    }

    private async Task ReleaseRefreshLockAsync(
        TwitchOAuthRefreshLockLease lease,
        string twitchUserId,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await lease.ReleaseAsync(cancellationToken);
        if (result.Status == TwitchOAuthRefreshLockReleaseStatus.OwnershipLost)
        {
            _logger.LogWarning(
                "Twitch OAuth refresh lock 在釋放前已過期或 owner 已變更，未刪除 lock | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }
        else if (result.Status == TwitchOAuthRefreshLockReleaseStatus.TemporaryFailure)
        {
            _logger.LogWarning(
                result.Exception,
                "Twitch OAuth refresh lock 暫時無法釋放，將由 TTL 清除 | Operation: {Operation} | TwitchUserId: {TwitchUserId}",
                operation,
                twitchUserId);
        }
    }

    /// <summary>僅在 MySQL 目前狀態仍符合預期版本時發布授權事件，忽略 relink 後的 stale publication。</summary>
    private async Task TryPublishAuthorizationChangedAsync(
        TwitchBroadcasterAuthorization expectedState,
        string status,
        CancellationToken cancellationToken)
    {
        // 非同步發布可能晚於 relink；MySQL 當前狀態必須仍符合 expectedState 才能送出事件。
        using var db = _dbContextFactory.CreateDbContext();
        var currentState = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TwitchUserId == expectedState.TwitchUserId, cancellationToken);
        if (!ShouldPublishAuthorizationChange(expectedState, currentState, status))
        {
            _logger.LogInformation(
                "略過已過時的 Twitch authorization_changed publication | TwitchUserId: {TwitchUserId} | Status: {Status}",
                expectedState.TwitchUserId,
                status);
            return;
        }

        var payload = JsonConvert.SerializeObject(new TwitchAuthorizationChangedPayload(currentState.TwitchUserId, status));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var subscriberCount = await _redisService.RedisSub.PublishAsync(
                new RedisChannel(RedisChannels.Twitch.AuthorizationChanged, RedisChannel.PatternMode.Literal),
                payload);
            if (subscriberCount == 0)
            {
                _logger.LogWarning(
                    "Twitch authorization_changed 沒有 Redis subscriber；MySQL 狀態將由定期 replay 補送 | TwitchUserId: {TwitchUserId} | Status: {Status}",
                    currentState.TwitchUserId,
                    status);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Twitch authorization_changed 暫時無法發布；MySQL 狀態將由定期 replay 補送 | TwitchUserId: {TwitchUserId} | Status: {Status}",
                currentState.TwitchUserId,
                status);
        }
    }

    internal static bool IsInvalidationStatus(string status)
        => string.Equals(status, "invalid", StringComparison.Ordinal) ||
            string.Equals(status, "revoked", StringComparison.Ordinal) ||
            string.Equals(status, "unlinked", StringComparison.Ordinal);

    internal static bool ShouldPublishAuthorizationChange(
        TwitchBroadcasterAuthorization expectedState,
        TwitchBroadcasterAuthorization currentState,
        string status)
        => currentState != null &&
            NormalizeMySqlDateTime(currentState.DateUpdated) == NormalizeMySqlDateTime(expectedState.DateUpdated) &&
            currentState.RevocationReason == expectedState.RevocationReason &&
            IsInvalidationStatus(status) == currentState.RevokedAt.HasValue;

    internal static bool IsRefreshedTokenPersistenceSatisfied(
        string currentEncryptedToken,
        string revocationReason,
        string pendingEncryptedToken)
        => currentEncryptedToken == pendingEncryptedToken &&
            (revocationReason == null || revocationReason == RevocationPendingReason);

    private static DateTime UtcNowForMySql() => NormalizeMySqlDateTime(DateTime.UtcNow);

    private static DateTime NormalizeMySqlDateTime(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

    private static void ThrowIfStateChanged(int affectedRows, string twitchUserId, string operation)
    {
        if (affectedRows != 1)
        {
            throw new DbUpdateConcurrencyException(
                $"Twitch authorization state changed during {operation}; stale write rejected for {twitchUserId}.");
        }
    }

    private bool TryRemovePendingRefreshedToken(string twitchUserId, PendingRefreshedToken pending)
        => ((ICollection<KeyValuePair<string, PendingRefreshedToken>>)_pendingRefreshedTokens)
            .Remove(new KeyValuePair<string, PendingRefreshedToken>(twitchUserId, pending));

    private async Task RegisterPendingRefreshedTokenAsync(string twitchUserId, PendingRefreshedToken pending)
    {
        while (!_pendingRefreshedTokens.TryAdd(twitchUserId, pending))
        {
            if (!_pendingRefreshedTokens.TryGetValue(twitchUserId, out var existing) ||
                !_pendingRefreshedTokens.TryUpdate(twitchUserId, pending, existing))
            {
                continue;
            }

            await ReleaseRefreshLockAsync(
                existing.Lease,
                twitchUserId,
                "superseded_pending_refresh",
                CancellationToken.None);
            break;
        }
    }

    /// <summary>停止接納新 refresh，等待執行中的 rotation 完成交接並 drain 所有 persistence task。</summary>
    public Task StopAcceptingAndDrainAsync()
    {
        lock (_stopGate)
            return _stopTask ??= StopCoreAsync();
    }

    /// <summary>執行 rotation lifecycle drain，定期記錄等待狀態並更新關機 persistence 指標。</summary>
    private async Task StopCoreAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var drainTask = _rotationLifecycle.StopAcceptingAndDrainAsync();
        var isDraining = _rotationLifecycle.ActiveOperationCount > 0 ||
            _rotationLifecycle.PendingPersistenceCount > 0;
        BackendMetrics.TwitchRefreshShutdownDraining.Set(isDraining ? 1 : 0);
        if (isDraining)
        {
            _logger.LogWarning(
                "Backend 正在等待已接受的 Twitch refresh rotation 保存完成 | Active: {ActiveCount} | Pending: {PendingCount}",
                _rotationLifecycle.ActiveOperationCount,
                _rotationLifecycle.PendingPersistenceCount);
        }

        try
        {
            while (!drainTask.IsCompleted)
            {
                var completed = await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(30)));
                if (completed != drainTask)
                {
                    _logger.LogWarning(
                        "Backend 關閉仍在等待 Twitch refresh rotation 保存 | Active: {ActiveCount} | Pending: {PendingCount} | ElapsedSeconds: {ElapsedSeconds:F1}",
                        _rotationLifecycle.ActiveOperationCount,
                        _rotationLifecycle.PendingPersistenceCount,
                        stopwatch.Elapsed.TotalSeconds);
                }
            }
            await drainTask;
        }
        finally
        {
            stopwatch.Stop();
            BackendMetrics.TwitchRefreshShutdownDraining.Set(0);
            BackendMetrics.TwitchRefreshShutdownDrainDuration.Observe(stopwatch.Elapsed.TotalSeconds);
        }

        if (isDraining)
        {
            _logger.LogInformation(
                "Backend 已保存全部接受的 Twitch refresh rotation | DrainSeconds: {DrainSeconds:F1}",
                stopwatch.Elapsed.TotalSeconds);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAcceptingAndDrainAsync();
        GC.SuppressFinalize(this);
    }

    private async Task UpdateMetricsAsync(CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var linked = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt == null, cancellationToken);
        var revoked = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt != null && x.RevocationReason == UserUnlinkedReason, cancellationToken);
        var invalid = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt != null && x.RevocationReason != UserUnlinkedReason, cancellationToken);
        BackendMetrics.OAuthLinkedAccounts.WithLabels("twitch", "linked").Set(linked);
        BackendMetrics.OAuthLinkedAccounts.WithLabels("twitch", "revoked").Set(revoked);
        BackendMetrics.OAuthLinkedAccounts.WithLabels("twitch", "invalid").Set(invalid);
    }

    private async Task TryUpdateMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UpdateMetricsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Twitch OAuth 帳號 metrics 更新失敗");
        }
    }
}

internal sealed class TwitchAuthorizationChangedPayload
{
    public TwitchAuthorizationChangedPayload(string twitchUserId, string status)
    {
        TwitchUserId = twitchUserId;
        Status = status;
    }

    [JsonProperty("TwitchUserId")]
    public string TwitchUserId { get; }

    [JsonProperty("Status")]
    public string Status { get; }
}

internal sealed class PendingRefreshedToken
{
    private int _retryQueued;

    public PendingRefreshedToken(
        string expectedEncryptedToken,
        string encryptedToken,
        DateTime? tokenExpiresAt,
        TwitchOAuthRefreshLockLease lease)
    {
        ExpectedEncryptedToken = expectedEncryptedToken;
        EncryptedToken = encryptedToken;
        TokenExpiresAt = tokenExpiresAt;
        Lease = lease;
    }

    public string ExpectedEncryptedToken { get; }
    public string EncryptedToken { get; }
    public DateTime? TokenExpiresAt { get; }
    public TwitchOAuthRefreshLockLease Lease { get; set; }
    public SemaphoreSlim PersistenceGate { get; } = new(1, 1);

    public bool TryMarkRetryQueued()
        => Interlocked.Exchange(ref _retryQueued, 1) == 0;
}

internal enum TwitchApiResultStatus
{
    Success,
    Invalid,
    TransientFailure,
    Failure
}

internal sealed class TwitchApiResult<T>
{
    public TwitchApiResultStatus Status { get; private init; }
    public T Value { get; private init; }

    public static TwitchApiResult<T> Success(T value) => new() { Status = TwitchApiResultStatus.Success, Value = value };
    public static TwitchApiResult<T> Invalid() => new() { Status = TwitchApiResultStatus.Invalid };
    public static TwitchApiResult<T> TransientFailure() => new() { Status = TwitchApiResultStatus.TransientFailure };
    public static TwitchApiResult<T> Failure() => new() { Status = TwitchApiResultStatus.Failure };
}
