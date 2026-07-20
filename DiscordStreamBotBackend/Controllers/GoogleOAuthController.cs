using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers;

[ApiController]
[Route("oauth/google")]
public class GoogleOAuthController : ControllerBase
{
    private readonly BearerTokenService _bearerTokenService;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly ILogger<GoogleOAuthController> _logger;
    private readonly PublicUrlService _publicUrls;
    private readonly OAuthStateService _stateService;

    public GoogleOAuthController(
        BearerTokenService bearerTokenService,
        GoogleOAuthService googleOAuthService,
        ILogger<GoogleOAuthController> logger,
        PublicUrlService publicUrls,
        OAuthStateService stateService)
    {
        _bearerTokenService = bearerTokenService;
        _googleOAuthService = googleOAuthService;
        _logger = logger;
        _publicUrls = publicUrls;
        _stateService = stateService;
    }

    [EnableCors("frontend")]
    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        if (!_bearerTokenService.TryGetDiscordUserId(Request.Headers.Authorization.ToString(), out var discordUserId))
        {
            BackendMetrics.OAuthAttempts.WithLabels("google", "unauthorized").Inc();
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });
        }

        var state = await _stateService.CreateAsync("google", discordUserId);
        BackendMetrics.OAuthAttempts.WithLabels("google", "started").Inc();
        return Ok(new { authorizationUrl = _googleOAuthService.GetAuthorizationUrl(state) });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state, string error, CancellationToken cancellationToken)
    {
        var stateData = await _stateService.ConsumeAsync("google", state);
        if (stateData == null)
            return RedirectError("invalid_state");
        if (!string.IsNullOrWhiteSpace(error))
            return RedirectError("authorization_denied");
        if (string.IsNullOrWhiteSpace(code))
            return RedirectError("missing_code");

        try
        {
            if (!await _googleOAuthService.CompleteAuthorizationAsync(stateData.DiscordUserId, code, cancellationToken))
                return RedirectError("token_exchange_failed");

            BackendMetrics.OAuthAttempts.WithLabels("google", "success").Inc();
            return Redirect(_publicUrls.GetFrontendReturnUrl("google", "success"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google OAuth callback 處理失敗");
            return RedirectError("server_error");
        }
    }

    private IActionResult RedirectError(string reason)
    {
        BackendMetrics.OAuthAttempts.WithLabels("google", reason).Inc();
        return Redirect(_publicUrls.GetFrontendReturnUrl("google", "error", reason));
    }
}
