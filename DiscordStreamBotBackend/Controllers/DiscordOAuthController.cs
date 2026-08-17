using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers;

[ApiController]
[Route("oauth/discord")]
public class DiscordOAuthController : ControllerBase
{
    private readonly BearerTokenService _bearerTokenService;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordOAuthController> _logger;
    private readonly PublicUrlService _publicUrls;

    public DiscordOAuthController(
        BearerTokenService bearerTokenService,
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<DiscordOAuthController> logger,
        PublicUrlService publicUrls)
    {
        _bearerTokenService = bearerTokenService;
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
        _publicUrls = publicUrls;
    }

    [EnableCors("frontend")]
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] DiscordOAuthCallbackRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.Code))
            return BadRequest(new { error = "missing_code", message = "Discord 登入回應缺少授權碼。" });

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = request.Code,
                ["client_id"] = _configuration["Discord:ClientId"],
                ["client_secret"] = _configuration["Discord:ClientSecret"],
                ["redirect_uri"] = _publicUrls.DiscordRedirectUrl,
                ["grant_type"] = "authorization_code"
            });
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var response = await _httpClient.PostAsync("https://discord.com/api/v10/oauth2/token", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return IsTemporaryProviderFailure(response.StatusCode)
                    ? StatusCode(503, new { error = "provider_unavailable", message = "Discord 暫時無法使用，請稍後再試。" })
                    : BadRequest(new { error = "token_exchange_failed", message = "Discord 登入失敗，請再試一次。" });

            var tokenData = JsonConvert.DeserializeObject<DiscordAccessTokenData>(await response.Content.ReadAsStringAsync(cancellationToken));
            if (string.IsNullOrWhiteSpace(tokenData?.AccessToken) || tokenData.ExpiresIn <= 0)
                return Unauthorized(new { error = "invalid_token", message = "Discord 驗證失敗，請重新登入。" });

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            using var userResponse = await _httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
                return IsTemporaryProviderFailure(userResponse.StatusCode)
                    ? StatusCode(503, new { error = "provider_unavailable", message = "Discord 暫時無法使用，請稍後再試。" })
                    : Unauthorized(new { error = "profile_fetch_failed", message = "無法取得 Discord 帳號資料，請重新登入。" });

            var discordUser = JsonConvert.DeserializeObject<DiscordUser>(await userResponse.Content.ReadAsStringAsync(cancellationToken));
            if (discordUser == null || !ulong.TryParse(discordUser.id, out var discordUserId))
                return Unauthorized(new { error = "invalid_profile", message = "無法取得 Discord 帳號資料，請重新登入。" });

            _logger.LogInformation("Discord 使用者 OAuth 完成: {DiscordUsername} ({DiscordUserId})", discordUser.username, discordUser.id);
            return Ok(new
            {
                token = _bearerTokenService.CreateDiscordSessionToken(discordUserId, tokenData.AccessToken, tokenData.ExpiresIn),
                discordData = discordUser
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Discord OAuth callback 等待 provider 回應逾時");
            return StatusCode(503, new { error = "provider_unavailable", message = "Discord 暫時無法使用，請稍後再試。" });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Discord OAuth callback 的 API 或 token 交換失敗");
            return StatusCode(503, new { error = "provider_unavailable", message = "Discord 暫時無法使用，請稍後再試。" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Discord OAuth callback 處理失敗");
            return StatusCode(500, new { error = "server_error", message = "伺服器發生錯誤，請稍後再試。" });
        }
    }

    private static bool IsTemporaryProviderFailure(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
