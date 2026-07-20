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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class GoogleOAuthService
{
    private const string Scope = "https://www.googleapis.com/auth/youtube.force-ssl";
    private readonly GoogleAuthorizationCodeFlow _flow;
    private readonly IDbContextFactory<MainDbContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleOAuthService> _logger;
    private readonly PublicUrlService _publicUrls;
    private readonly RedisService _redisService;
    private readonly string _clientId;

    public GoogleOAuthService(
        IConfiguration configuration,
        IDbContextFactory<MainDbContext> dbContextFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleOAuthService> logger,
        PublicUrlService publicUrls,
        RedisService redisService,
        TokenService tokenService)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _publicUrls = publicUrls;
        _redisService = redisService;
        _clientId = configuration["Google:ClientId"];
        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = _clientId,
                ClientSecret = configuration["Google:ClientSecret"]
            },
            Scopes = [Scope],
            DataStore = new MySqlDataStore(dbContextFactory, tokenService)
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
        var key = discordUserId.ToString();
        var existing = await _flow.LoadTokenAsync(key, cancellationToken);
        var token = await _flow.ExchangeCodeForTokenAsync(key, code, redirectUrl, cancellationToken);
        if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
            return false;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
            token.RefreshToken = existing?.RefreshToken;

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            await _flow.DeleteTokenAsync(key, cancellationToken);
            return false;
        }

        await _flow.DataStore.StoreAsync(key, token);
        await TryUpdateMetricsAsync(CancellationToken.None);
        return true;
    }

    public async Task<GoogleAccountLink> GetAccountLinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var key = discordUserId.ToString();
        var token = await _flow.LoadTokenAsync(key, cancellationToken);
        if (token == null)
            return new GoogleAccountLink { Status = "unlinked" };

        if (string.IsNullOrWhiteSpace(token.AccessToken) || string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            BackendMetrics.OAuthTokenValidations.WithLabels("google", "invalid").Inc();
            return new GoogleAccountLink { Status = "invalid" };
        }

        try
        {
            if (IsExpired(token))
            {
                token = await _flow.RefreshTokenAsync(key, token.RefreshToken, cancellationToken);
                BackendMetrics.OAuthTokenRefreshes.WithLabels("google", "success").Inc();
            }

            var channel = await GetChannelAsync(token.AccessToken, cancellationToken);
            if (channel == null)
            {
                BackendMetrics.OAuthTokenValidations.WithLabels("google", "invalid").Inc();
                return new GoogleAccountLink { Status = "invalid" };
            }

            using var db = _dbContextFactory.CreateDbContext();
            var subscriptions = await db.YoutubeMemberCheck.AsNoTracking()
                .Where(x => x.UserId == discordUserId)
                .Select(x => new GoogleMemberSubscription
                {
                    GuildId = x.GuildId,
                    ChannelId = x.CheckYtChannelId,
                    IsChecked = x.IsChecked,
                    LastCheckedAt = x.LastCheckTime
                })
                .ToListAsync(cancellationToken);

            BackendMetrics.OAuthTokenValidations.WithLabels("google", "valid").Inc();
            return new GoogleAccountLink
            {
                Status = "linked",
                ChannelId = channel.id,
                UserName = channel.snippet.title,
                ProfileImageUrl = channel.snippet.thumbnails?.@default?.url,
                Subscriptions = subscriptions
            };
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

    public async Task<bool> UnlinkAsync(ulong discordUserId, CancellationToken cancellationToken)
    {
        var key = discordUserId.ToString();
        var token = await _flow.LoadTokenAsync(key, cancellationToken);
        if (token == null)
            return true;

        var revokeToken = token.RefreshToken ?? token.AccessToken;
        if (string.IsNullOrWhiteSpace(revokeToken))
        {
            _logger.LogWarning("Google provider 撤銷失敗，本機 token 缺少可撤銷內容");
            return false;
        }

        try
        {
            await _flow.RevokeTokenAsync(key, revokeToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google provider 撤銷失敗，保留本機 token 待稍後重試");
            return false;
        }

        await _flow.DeleteTokenAsync(key, cancellationToken);
        await _redisService.AddPubMessageAsync("member.revokeToken", key, CancellationToken.None);
        await TryUpdateMetricsAsync(CancellationToken.None);
        return true;
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
