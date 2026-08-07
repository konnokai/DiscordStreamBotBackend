using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class GoogleOAuthService : IGoogleAccountProvider, IGoogleProviderRevoker, IGoogleAccountLinkMetricsRefresher
{
    private const string Scope = "https://www.googleapis.com/auth/youtube.force-ssl";
    private readonly GoogleAccountOperationCoordinator _coordinator;
    private readonly IGoogleOAuthOperationLock _distributedOperationLock;
    private readonly MySqlDataStore _dataStore;
    private readonly GoogleAuthorizationCodeFlow _flow;
    private readonly IDbContextFactory<MainDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleOAuthService> _logger;
    private readonly PublicUrlService _publicUrls;
    private readonly string _clientId;

    public GoogleOAuthService(
        IConfiguration configuration,
        IDbContextFactory<MainDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleOAuthService> logger,
        PublicUrlService publicUrls,
        TokenService tokenService,
        GoogleAccountOperationCoordinator coordinator,
        IGoogleOAuthOperationLock distributedOperationLock)
    {
        _coordinator = coordinator;
        _distributedOperationLock = distributedOperationLock;
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _publicUrls = publicUrls;
        _clientId = configuration["Google:ClientId"];
        _dataStore = new MySqlDataStore(dbContextFactory, tokenService);
        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _clientId,
                ClientSecret = configuration["Google:ClientSecret"]
            },
            Scopes = [Scope],
            DataStore = new NonPersistentGoogleDataStore()
        });
    }

    public string GetAuthorizationUrl(string state)
    {
        return QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string>
        {
            ["scope"] = Scope,
            ["access_type"] = "offline",
            ["include_granted_scopes"] = "true",
            ["response_type"] = "code",
            ["state"] = state,
            ["redirect_uri"] = _publicUrls.GoogleCallbackUrl,
            ["client_id"] = _clientId,
            ["prompt"] = "consent"
        });
    }

    public async Task<bool> CompleteAuthorizationAsync(ulong discordUserId, string code, CancellationToken cancellationToken)
        => await CompleteAuthorizationAsync(discordUserId, code, _publicUrls.GoogleCallbackUrl, cancellationToken);

    public async Task<bool> CompleteAuthorizationAsync(ulong discordUserId, string code, string redirectUrl, CancellationToken cancellationToken)
    {
        using var operationLease = await _coordinator.AcquireAsync(discordUserId, cancellationToken);
        GoogleOAuthOperationLockAcquireResult lockResult = await _distributedOperationLock.TryAcquireAsync(
            discordUserId, cancellationToken);
        if (lockResult.Status != GoogleOAuthOperationLockAcquireStatus.Acquired)
        {
            _logger.LogWarning(
                lockResult.Exception,
                "Google callback 無法取得跨程序 OAuth lease | DiscordUserId: {DiscordUserId} | Status: {Status}",
                discordUserId,
                lockResult.Status);
            return false;
        }
        await using var distributedLease = lockResult.Lease;
        var key = discordUserId.ToString();
        if (await _dataStore.HasUnlinkIntentAsync(discordUserId, cancellationToken))
            return false;
        var existingLoad = await _dataStore.LoadAsync<TokenResponse>(key, cancellationToken);
        var existing = existingLoad.Status == ProviderTokenLoadStatus.Loaded
            ? existingLoad.Value
            : null;
        var token = await _flow.ExchangeCodeForTokenAsync(key, code, redirectUrl, cancellationToken);
        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            return false;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            token.RefreshToken = existing?.RefreshToken;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            return false;

        if (await distributedLease.EnsureOwnedAsync(cancellationToken) !=
            GoogleOAuthOperationLockOwnershipStatus.Owned)
        {
            return false;
        }
        if (!await _dataStore.StoreAuthorizationIfNoUnlinkIntentAsync(
            discordUserId, token, cancellationToken))
        {
            return false;
        }
        await TryUpdateMetricsAsync(CancellationToken.None);
        return true;
    }

    public async Task<GoogleAccountLink> GetProviderAccountAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        using var operationLease = await _coordinator.AcquireAsync(discordUserId, cancellationToken);
        var key = discordUserId.ToString();
        var loadResult = await _dataStore.LoadAsync<TokenResponse>(key, cancellationToken);
        if (loadResult.Status == ProviderTokenLoadStatus.Missing)
            return new GoogleAccountLink { Status = "unlinked" };
        if (loadResult.Status == ProviderTokenLoadStatus.Unreadable)
        {
            BackendMetrics.OAuthTokenValidations.WithLabels("google", "invalid").Inc();
            return new GoogleAccountLink { Status = "invalid" };
        }

        var token = loadResult.Value;

        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            BackendMetrics.OAuthTokenValidations.WithLabels("google", "invalid").Inc();
            return new GoogleAccountLink { Status = "invalid" };
        }

        try
        {
            if (IsExpired(token))
            {
                GoogleOAuthOperationLockAcquireResult lockResult = await _distributedOperationLock.TryAcquireAsync(
                    discordUserId, cancellationToken);
                if (lockResult.Status != GoogleOAuthOperationLockAcquireStatus.Acquired)
                {
                    _logger.LogWarning(
                        lockResult.Exception,
                        "Google refresh 無法取得跨程序 OAuth lease | DiscordUserId: {DiscordUserId} | Status: {Status}",
                        discordUserId,
                        lockResult.Status);
                    return new GoogleAccountLink { Status = "invalid" };
                }
                await using var distributedLease = lockResult.Lease;
                loadResult = await _dataStore.LoadAsync<TokenResponse>(key, cancellationToken);
                if (loadResult.Status != ProviderTokenLoadStatus.Loaded)
                    return new GoogleAccountLink { Status = "invalid" };
                if (await _dataStore.HasUnlinkIntentAsync(discordUserId, cancellationToken))
                    return new GoogleAccountLink { Status = "invalid" };
                token = loadResult.Value;
                if (IsExpired(token))
                {
                    if (await distributedLease.EnsureOwnedAsync(cancellationToken) !=
                        GoogleOAuthOperationLockOwnershipStatus.Owned)
                    {
                        return new GoogleAccountLink { Status = "invalid" };
                    }
                    string expectedEncryptedToken = loadResult.EncryptedPayload;
                    token = await _flow.RefreshTokenAsync(key, token.RefreshToken, cancellationToken);
                    token.RefreshToken ??= loadResult.Value.RefreshToken;
                    if (await distributedLease.EnsureOwnedAsync(cancellationToken) !=
                            GoogleOAuthOperationLockOwnershipStatus.Owned ||
                        !await _dataStore.StoreRefreshIfCurrentAsync(
                            discordUserId,
                            expectedEncryptedToken,
                            token,
                            cancellationToken))
                    {
                        return new GoogleAccountLink { Status = "invalid" };
                    }
                    BackendMetrics.OAuthTokenRefreshes.WithLabels("google", "success").Inc();
                }
            }

            var channel = await GetChannelAsync(token.AccessToken, cancellationToken);
            if (channel == null)
            {
                BackendMetrics.OAuthTokenValidations.WithLabels("google", "invalid").Inc();
                return new GoogleAccountLink { Status = "invalid" };
            }

            BackendMetrics.OAuthTokenValidations.WithLabels("google", "valid").Inc();
            return new GoogleAccountLink
            {
                Status = "linked",
                ChannelId = channel.id,
                UserName = channel.snippet.title,
                ProfileImageUrl = channel.snippet.thumbnails?.@default?.url
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            BackendMetrics.OAuthTokenValidations.WithLabels("google", "error").Inc();
            if (IsExpired(token))
                BackendMetrics.OAuthTokenRefreshes.WithLabels("google", "failure").Inc();
            _logger.LogWarning(ex, "Google OAuth token 驗證失敗");
            return new GoogleAccountLink { Status = "invalid" };
        }
    }

    async Task<GoogleProviderRevokeResult> IGoogleProviderRevoker.RevokeAsync(
        ulong discordUserId,
        string expectedEncryptedToken,
        CancellationToken cancellationToken)
    {
        var key = discordUserId.ToString();
        var loadResult = await _dataStore.LoadAsync<TokenResponse>(key, cancellationToken);
        if (loadResult.Status == ProviderTokenLoadStatus.Missing)
        {
            return expectedEncryptedToken == null
                ? new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.NoGrant, null)
                : new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.TokenChanged, null);
        }
        if (!string.Equals(loadResult.EncryptedPayload, expectedEncryptedToken, StringComparison.Ordinal))
            return new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.TokenChanged, null);
        if (loadResult.Status == ProviderTokenLoadStatus.Unreadable)
        {
            _logger.LogWarning("Google provider 撤銷失敗，本機 token 無法讀取；保留 token 與會員檢查");
            return new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.TokenUnreadable, null);
        }

        var token = loadResult.Value;

        var revokeToken = token.RefreshToken ?? token.AccessToken;
        if (string.IsNullOrWhiteSpace(revokeToken))
        {
            _logger.LogWarning("Google provider 撤銷失敗，本機 token 缺少可撤銷內容");
            return new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.Failed, null);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/revoke")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = revokeToken
                })
            };
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsConclusiveAlreadyRevoked((int)response.StatusCode, responseBody))
                {
                    return new GoogleProviderRevokeResult(
                        GoogleProviderRevokeOutcome.Revoked,
                        loadResult.EncryptedPayload);
                }
                _logger.LogWarning(
                    "Google provider 撤銷失敗，保留本機 token 待稍後重試 | StatusCode: {StatusCode}",
                    (int)response.StatusCode);
                return new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.Failed, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google provider 撤銷失敗，保留本機 token 待稍後重試");
            return new GoogleProviderRevokeResult(GoogleProviderRevokeOutcome.Failed, null);
        }

        return new GoogleProviderRevokeResult(
            GoogleProviderRevokeOutcome.Revoked,
            loadResult.EncryptedPayload);
    }

    internal static bool IsConclusiveAlreadyRevoked(int statusCode, string responseBody)
    {
        if (statusCode < (int)HttpStatusCode.BadRequest || statusCode >= 500 ||
            string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            return string.Equals(
                JObject.Parse(responseBody).Value<string>("error"),
                "invalid_token",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task UpdateMetricsAsync(CancellationToken cancellationToken)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var linked = await db.YoutubeMemberAccessToken.AsNoTracking().CountAsync(cancellationToken);
        BackendMetrics.OAuthLinkedAccounts.WithLabels("google", "linked").Set(linked);
    }

    private async Task TryUpdateMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UpdateMetricsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google OAuth 帳號 metrics 更新失敗");
        }
    }

    private async Task<Item> GetChannelAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://youtube.googleapis.com/youtube/v3/channels?part=id%2Csnippet&mine=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var data = JsonConvert.DeserializeObject<YoutubeChannelMeJson>(await response.Content.ReadAsStringAsync(cancellationToken));
        return data?.items?.FirstOrDefault();
    }

    private static bool IsExpired(TokenResponse token)
    {
        return !token.ExpiresInSeconds.HasValue || token.IssuedUtc.AddSeconds(token.ExpiresInSeconds.Value) <= DateTime.UtcNow.AddMinutes(1);
    }
}
