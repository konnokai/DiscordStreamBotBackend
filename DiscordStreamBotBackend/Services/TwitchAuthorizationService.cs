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
using System.Collections.Generic;
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

public class TwitchAuthorizationService
{
    private const string LegacyAuthorizationKeyPrefix = "twitch:oauth:";
    private const string RequiredScope = "user:read:subscriptions";
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly IDbContextFactory<MainDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwitchAuthorizationService> _logger;
    private readonly PublicUrlService _publicUrls;
    private readonly RedisService _redisService;
    private readonly TokenService _tokenService;

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

        var profile = await GetUserProfileAsync(token.AccessToken, cancellationToken);
        if (profile == null || profile.Id != validation.UserId)
            return "provider_validation_failed";

        using var db = _dbContextFactory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var byDiscord = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        var byTwitch = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.TwitchUserId == validation.UserId, cancellationToken);

        if (byDiscord != null && byDiscord.TwitchUserId != validation.UserId)
        {
            if (byDiscord.RevocationReason != "user_unlinked")
                return "account_conflict";
            db.TwitchBroadcasterAuthorization.Remove(byDiscord);
            byDiscord = null;
        }

        if (byTwitch != null && byTwitch.DiscordUserId != discordUserId)
        {
            if (byTwitch.RevocationReason != "user_unlinked")
                return "account_conflict";
            db.TwitchBroadcasterAuthorization.Remove(byTwitch);
            byTwitch = null;
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
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
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Twitch OAuth 帳號衝突");
            return "account_conflict";
        }

        await _redisService.RedisDb.KeyDeleteAsync(GetLegacyAuthorizationKey(discordUserId));
        await PublishAuthorizationChangedAsync(entity.TwitchUserId, "linked", CancellationToken.None);
        await TryUpdateMetricsAsync(CancellationToken.None);
        return null;
    }

    public async Task<TwitchAccountLink> GetAccountLinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var entity = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        if (entity == null)
        {
            var hasLegacyAuthorization = await _redisService.RedisDb.KeyExistsAsync(GetLegacyAuthorizationKey(discordUserId));
            return new TwitchAccountLink { Status = hasLegacyAuthorization ? "invalid" : "unlinked" };
        }

        var status = entity.RevokedAt == null
            ? entity.ClientId == _clientId && !string.IsNullOrWhiteSpace(entity.EncryptedAccessToken) ? "linked" : "invalid"
            : entity.RevocationReason == "user_unlinked" ? "revoked" : "invalid";
        return new TwitchAccountLink
        {
            Status = status,
            TwitchUserId = entity.TwitchUserId,
            UserLogin = entity.UserLogin,
            DisplayName = entity.DisplayName,
            ProfileImageUrl = entity.ProfileImageUrl
        };
    }

    public async Task<TwitchUnlinkResult> UnlinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var entity = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        if (entity == null)
            return await UnlinkLegacyAuthorizationAsync(discordUserId, cancellationToken);
        if (entity.RevocationReason == "user_unlinked")
            return TwitchUnlinkResult.Unlinked;

        var revokeStatus = entity.RevokedAt != null && entity.RevocationReason != "revocation_pending"
            ? TwitchApiResultStatus.Invalid
            : TwitchApiResultStatus.TransientFailure;
        if (!string.IsNullOrWhiteSpace(entity.EncryptedAccessToken))
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
            await FinalizeUnlinkAsync(entity, db, cancellationToken);
            await TryUpdateMetricsAsync(CancellationToken.None);
            return TwitchUnlinkResult.Unlinked;
        }

        await MarkRevocationPendingAsync(entity, db, cancellationToken);
        await TryUpdateMetricsAsync(CancellationToken.None);
        return TwitchUnlinkResult.RevocationPending;
    }

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

        await UpdateMetricsAsync(cancellationToken);
    }

    public async Task MigrateLegacyAuthorizationsAsync(CancellationToken cancellationToken)
    {
        var legacyKeys = new HashSet<string>(StringComparer.Ordinal);
        var scannedServerCount = 0;
        foreach (var endpoint in _redisService.Redis.GetEndPoints())
        {
            try
            {
                var server = _redisService.Redis.GetServer(endpoint);
                if (server.IsReplica)
                    continue;

                foreach (var key in server.Keys(_redisService.RedisDb.Database, $"{LegacyAuthorizationKeyPrefix}*", pageSize: 250))
                    legacyKeys.Add(key.ToString());
                scannedServerCount++;
            }
            catch (Exception ex) when (ex is RedisException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "掃描舊 Twitch OAuth token 失敗，將於下次 Backend 啟動時重試");
            }
        }

        if (scannedServerCount == 0)
        {
            _logger.LogWarning("沒有可完成 SCAN 的 Redis primary，舊 Twitch OAuth token 將於下次 Backend 啟動時重試");
            return;
        }

        if (legacyKeys.Count == 0)
            return;
        if (legacyKeys.Count > 1)
        {
            _logger.LogError(
                "偵測到 {LegacyTokenCount} 筆舊 Twitch OAuth token，超過已確認的單筆資料；為避免錯誤綁定，停止自動遷移並要求人工確認",
                legacyKeys.Count);
            return;
        }

        var completedCount = 0;
        foreach (var key in legacyKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryMigrateLegacyAuthorizationAsync(key, cancellationToken))
                    completedCount++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "舊 Twitch OAuth token 遷移失敗，保留 Redis 資料並於下次 Backend 啟動時重試");
            }
        }

        _logger.LogInformation(
            "舊 Twitch OAuth token 遷移完成 | 掃描: {ScannedCount} | 已完成: {CompletedCount} | 待重試: {PendingCount}",
            legacyKeys.Count,
            completedCount,
            legacyKeys.Count - completedCount);
    }

    private async Task<bool> TryMigrateLegacyAuthorizationAsync(string legacyKey, CancellationToken cancellationToken)
    {
        if (!legacyKey.StartsWith(LegacyAuthorizationKeyPrefix, StringComparison.Ordinal) ||
            !ulong.TryParse(legacyKey[LegacyAuthorizationKeyPrefix.Length..], out var discordUserId))
        {
            _logger.LogWarning("略過格式錯誤的舊 Twitch OAuth Redis key");
            return false;
        }

        var encryptedToken = await _redisService.RedisDb.StringGetAsync(legacyKey);
        if (!encryptedToken.HasValue)
            return true;

        TwitchAccessTokenData token;
        try
        {
            token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(encryptedToken.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "舊 Twitch OAuth token 無法解密，保留 Redis 資料供人工確認");
            return false;
        }

        var validationResult = await ValidateTokenAsync(token?.AccessToken, cancellationToken);
        if (validationResult.Status == TwitchApiResultStatus.Invalid)
        {
            // Twitch 已接受 refresh 後可能立即 rotation；正常關閉也必須先把新 token CAS 保存回 Redis。
            var refreshResult = await RefreshTokenAsync(token?.RefreshToken, CancellationToken.None);
            if (refreshResult.Status != TwitchApiResultStatus.Success)
            {
                _logger.LogWarning("舊 Twitch OAuth token 已失效且無法刷新，保留 Redis 資料供使用者重新授權");
                return false;
            }

            if (string.IsNullOrWhiteSpace(refreshResult.Value.RefreshToken))
                refreshResult.Value.RefreshToken = token.RefreshToken;
            token = refreshResult.Value;
            var refreshedEncryptedToken = _tokenService.CreateTokenResponseToken(token);
            if (!await ReplaceLegacyTokenIfUnchangedAsync(legacyKey, encryptedToken, refreshedEncryptedToken))
            {
                _logger.LogWarning("舊 Twitch OAuth token 在刷新期間已被更新，保留最新 Redis 資料並於下次重試");
                return false;
            }

            encryptedToken = refreshedEncryptedToken;
            validationResult = await ValidateTokenAsync(token.AccessToken, cancellationToken);
        }

        if (validationResult.Status != TwitchApiResultStatus.Success)
        {
            _logger.LogWarning("舊 Twitch OAuth token 暫時無法驗證，保留 Redis 資料並於下次重試");
            return false;
        }

        var validation = validationResult.Value;
        if (validation.ClientId != _clientId ||
            string.IsNullOrWhiteSpace(validation.UserId) ||
            validation.Scopes == null ||
            !validation.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
        {
            _logger.LogWarning("舊 Twitch OAuth token 與目前 Twitch Application 或必要 scope 不相容，保留 Redis 資料供人工確認");
            return false;
        }

        var profile = await GetUserProfileAsync(token.AccessToken, cancellationToken);
        if (profile == null || profile.Id != validation.UserId)
        {
            _logger.LogWarning("舊 Twitch OAuth token 無法取得一致的使用者資料，保留 Redis 資料並於下次重試");
            return false;
        }

        using var db = _dbContextFactory.CreateDbContext();
        var byDiscord = await db.TwitchBroadcasterAuthorization
            .SingleOrDefaultAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        var byTwitch = await db.TwitchBroadcasterAuthorization
            .SingleOrDefaultAsync(x => x.TwitchUserId == validation.UserId, cancellationToken);
        if (byDiscord != null || byTwitch != null)
        {
            if (byDiscord?.TwitchUserId == validation.UserId && byTwitch?.DiscordUserId == discordUserId)
            {
                if (byDiscord.RevokedAt != null)
                {
                    var revokeStatus = await RevokeTokenAsync(token.AccessToken, cancellationToken);
                    if (revokeStatus != TwitchApiResultStatus.Success && revokeStatus != TwitchApiResultStatus.Invalid)
                    {
                        _logger.LogWarning("MySQL 授權已撤銷，但舊 Twitch OAuth token 暫時無法撤銷，保留 Redis 資料供人工確認");
                        return false;
                    }
                }

                // MySQL row 代表較新的權威狀態，不可讓 legacy token 覆寫新 OAuth 或撤銷結果。
                return await DeleteLegacyTokenIfUnchangedAsync(legacyKey, encryptedToken);
            }

            _logger.LogWarning("舊 Twitch OAuth token 與既有 MySQL 授權資料衝突，保留 Redis 資料供人工確認");
            return false;
        }

        var now = DateTime.UtcNow;
        db.TwitchBroadcasterAuthorization.Add(new TwitchBroadcasterAuthorization
        {
            TwitchUserId = validation.UserId,
            DiscordUserId = discordUserId,
            ClientId = validation.ClientId,
            UserLogin = profile.Login,
            DisplayName = profile.DisplayName,
            ProfileImageUrl = profile.ProfileImageUrl,
            EncryptedAccessToken = encryptedToken.ToString(),
            Scopes = JsonConvert.SerializeObject(validation.Scopes),
            TokenExpiresAt = validation.ExpiresIn > 0 ? now.AddSeconds(validation.ExpiresIn) : null,
            LastValidatedAt = now,
            AuthorizedAt = now,
            DateUpdated = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "舊 Twitch OAuth token 寫入 MySQL 時發生帳號衝突，保留 Redis 資料供人工確認");
            return false;
        }

        await PublishAuthorizationChangedAsync(validation.UserId, "linked", cancellationToken);
        return await DeleteLegacyTokenIfUnchangedAsync(legacyKey, encryptedToken);
    }

    private async Task<TwitchUnlinkResult> UnlinkLegacyAuthorizationAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var legacyKey = GetLegacyAuthorizationKey(discordUserId);
        var encryptedToken = await _redisService.RedisDb.StringGetAsync(legacyKey);
        if (!encryptedToken.HasValue)
            return TwitchUnlinkResult.Unlinked;

        TwitchAccessTokenData token;
        try
        {
            token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(encryptedToken.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "舊 Twitch OAuth token 解密失敗，保留 Redis 資料供使用者重試解除連結");
            return TwitchUnlinkResult.RevocationPending;
        }

        var revokeStatus = await RevokeTokenAsync(token?.AccessToken, cancellationToken);
        if (revokeStatus != TwitchApiResultStatus.Success && revokeStatus != TwitchApiResultStatus.Invalid)
            return TwitchUnlinkResult.RevocationPending;

        if (!await DeleteLegacyTokenIfUnchangedAsync(legacyKey, encryptedToken))
            return TwitchUnlinkResult.RevocationPending;

        return TwitchUnlinkResult.Unlinked;
    }

    private async Task<bool> ReplaceLegacyTokenIfUnchangedAsync(string key, RedisValue expectedValue, RedisValue newValue)
    {
        var transaction = _redisService.RedisDb.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, expectedValue));
        _ = transaction.StringSetAsync(key, newValue);
        return await transaction.ExecuteAsync();
    }

    private async Task<bool> DeleteLegacyTokenIfUnchangedAsync(string key, RedisValue expectedValue)
    {
        var transaction = _redisService.RedisDb.CreateTransaction();
        transaction.AddCondition(Condition.StringEqual(key, expectedValue));
        _ = transaction.KeyDeleteAsync(key);
        return await transaction.ExecuteAsync();
    }

    private static string GetLegacyAuthorizationKey(ulong discordUserId)
        => $"{LegacyAuthorizationKeyPrefix}{discordUserId}";

    private async Task ValidateStoredAuthorizationAsync(string twitchUserId, CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var entity = await db.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.TwitchUserId == twitchUserId, cancellationToken);
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
            var refreshResult = await RefreshTokenAsync(token?.RefreshToken, cancellationToken);
            if (refreshResult.Status == TwitchApiResultStatus.Invalid)
            {
                BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "failure").Inc();
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
            token = refreshedToken;
            validationResult = await ValidateTokenAsync(token.AccessToken, cancellationToken);
            refreshed = true;
            BackendMetrics.OAuthTokenRefreshes.WithLabels("twitch", "success").Inc();

            if (validationResult.Status == TwitchApiResultStatus.TransientFailure || validationResult.Status == TwitchApiResultStatus.Failure)
            {
                await SaveRefreshedTokenForRetryAsync(entity, token, db, cancellationToken);
                BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "temporary_failure").Inc();
                return;
            }
        }

        if (validationResult.Status == TwitchApiResultStatus.Invalid)
        {
            await MarkRevokedAsync(entity, "token_invalid", db, cancellationToken);
            return;
        }
        var validation = validationResult.Value;
        if (validation.ClientId != _clientId)
        {
            await MarkRevokedAsync(entity, "client_id_mismatch", db, cancellationToken);
            return;
        }
        if (validation.UserId != entity.TwitchUserId)
        {
            await MarkRevokedAsync(entity, "user_id_mismatch", db, cancellationToken);
            return;
        }
        if (validation.Scopes == null || !validation.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
        {
            await MarkRevokedAsync(entity, "scope_mismatch", db, cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        if (refreshed)
            entity.EncryptedAccessToken = _tokenService.CreateTokenResponseToken(token);
        entity.UserLogin = validation.Login ?? entity.UserLogin;
        entity.Scopes = JsonConvert.SerializeObject(validation.Scopes ?? []);
        entity.TokenExpiresAt = now.AddSeconds(validation.ExpiresIn);
        entity.LastValidatedAt = now;
        entity.DateUpdated = now;
        await db.SaveChangesAsync(cancellationToken);
        BackendMetrics.OAuthTokenValidations.WithLabels("twitch", refreshed ? "refreshed" : "valid").Inc();
    }

    private async Task MarkRevokedAsync(TwitchBroadcasterAuthorization entity, string reason, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        entity.EncryptedAccessToken = null;
        entity.TokenExpiresAt = null;
        entity.RevokedAt = now;
        entity.RevocationReason = reason;
        entity.DateUpdated = now;
        await db.SaveChangesAsync(cancellationToken);
        BackendMetrics.OAuthTokenValidations.WithLabels("twitch", "revoked").Inc();
        await PublishAuthorizationChangedAsync(entity.TwitchUserId, "invalid", cancellationToken);
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

    private async Task RetryPendingRevocationsAsync(CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var userIds = await db.TwitchBroadcasterAuthorization.AsNoTracking()
            .Where(x => x.RevocationReason == "revocation_pending" && x.EncryptedAccessToken != null)
            .Select(x => x.TwitchUserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                using var retryDb = _dbContextFactory.CreateDbContext();
                var entity = await retryDb.TwitchBroadcasterAuthorization.SingleOrDefaultAsync(x => x.TwitchUserId == userId, cancellationToken);
                if (entity?.RevocationReason != "revocation_pending" || string.IsNullOrWhiteSpace(entity.EncryptedAccessToken))
                    continue;

                var token = _tokenService.GetTokenResponseValue<TwitchAccessTokenData>(entity.EncryptedAccessToken);
                if (string.IsNullOrWhiteSpace(token?.AccessToken))
                    continue;

                var result = await RevokeTokenAsync(token.AccessToken, cancellationToken);
                if (result == TwitchApiResultStatus.Success || result == TwitchApiResultStatus.Invalid)
                    await FinalizeUnlinkAsync(entity, retryDb, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twitch pending revoke 重試失敗，使用者: {TwitchUserId}", userId);
            }
        }
    }

    private async Task MarkRevocationPendingAsync(TwitchBroadcasterAuthorization entity, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        entity.RevokedAt = now;
        entity.RevocationReason = "revocation_pending";
        entity.DateUpdated = now;
        await db.SaveChangesAsync(cancellationToken);
        await PublishAuthorizationChangedAsync(entity.TwitchUserId, "invalid", cancellationToken);
    }

    private async Task FinalizeUnlinkAsync(TwitchBroadcasterAuthorization entity, MainDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        entity.EncryptedAccessToken = null;
        entity.TokenExpiresAt = null;
        entity.RevokedAt = now;
        entity.RevocationReason = "user_unlinked";
        entity.DateUpdated = now;
        await db.SaveChangesAsync(cancellationToken);
        await PublishAuthorizationChangedAsync(entity.TwitchUserId, "invalid", cancellationToken);
    }

    private async Task SaveRefreshedTokenForRetryAsync(
        TwitchBroadcasterAuthorization entity,
        TwitchAccessTokenData token,
        MainDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        entity.EncryptedAccessToken = _tokenService.CreateTokenResponseToken(token);
        if (token.ExpiresIn > 0)
            entity.TokenExpiresAt = now.AddSeconds(token.ExpiresIn);
        entity.DateUpdated = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task PublishAuthorizationChangedAsync(string twitchUserId, string status, CancellationToken cancellationToken)
    {
        var payload = JsonConvert.SerializeObject(new { TwitchUserId = twitchUserId, Status = status });
        return _redisService.AddPubMessageAsync(RedisChannels.Twitch.AuthorizationChanged, payload, cancellationToken).AsTask();
    }

    private async Task UpdateMetricsAsync(CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var linked = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt == null, cancellationToken);
        var revoked = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt != null && x.RevocationReason == "user_unlinked", cancellationToken);
        var invalid = await db.TwitchBroadcasterAuthorization.AsNoTracking().CountAsync(x => x.RevokedAt != null && x.RevocationReason != "user_unlinked", cancellationToken);
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
