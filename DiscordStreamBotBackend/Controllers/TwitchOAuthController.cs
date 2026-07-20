using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers;

[ApiController]
[Route("oauth/twitch")]
public class TwitchOAuthController : ControllerBase
{
    private readonly BearerTokenService _bearerTokenService;
    private readonly ILogger<TwitchOAuthController> _logger;
    private readonly OAuthStateService _stateService;
    private readonly TwitchAuthorizationService _twitchAuthorizationService;
    private readonly PublicUrlService _publicUrls;

    public TwitchOAuthController(
        BearerTokenService bearerTokenService,
        ILogger<TwitchOAuthController> logger,
        OAuthStateService stateService,
        TwitchAuthorizationService twitchAuthorizationService,
        PublicUrlService publicUrls)
    {
        _bearerTokenService = bearerTokenService;
        _logger = logger;
        _stateService = stateService;
        _twitchAuthorizationService = twitchAuthorizationService;
        _publicUrls = publicUrls;
    }

    [EnableCors("frontend")]
    [HttpPost("start")]
    public async Task<IActionResult> Start()
    {
        if (!_bearerTokenService.TryGetDiscordUserId(Request.Headers.Authorization.ToString(), out var discordUserId))
        {
            BackendMetrics.OAuthAttempts.WithLabels("twitch", "unauthorized").Inc();
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });
        }

        var state = await _stateService.CreateAsync("twitch", discordUserId);
        BackendMetrics.OAuthAttempts.WithLabels("twitch", "started").Inc();
        return Ok(new { authorizationUrl = _twitchAuthorizationService.GetAuthorizationUrl(state) });
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state, string error, CancellationToken cancellationToken)
    {
        var stateData = await _stateService.ConsumeAsync("twitch", state);
        if (stateData == null)
            return RedirectError("invalid_state");
        if (!string.IsNullOrWhiteSpace(error))
            return RedirectError("authorization_denied");
        if (string.IsNullOrWhiteSpace(code))
            return RedirectError("missing_code");

        try
        {
            var reason = await _twitchAuthorizationService.CompleteAuthorizationAsync(stateData.DiscordUserId, code, cancellationToken);
            if (reason != null)
                return RedirectError(reason);

            BackendMetrics.OAuthAttempts.WithLabels("twitch", "success").Inc();
            return Redirect(_publicUrls.GetFrontendReturnUrl("twitch", "success"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitch OAuth callback 處理失敗");
            return RedirectError("server_error");
        }
    }

    private IActionResult RedirectError(string reason)
    {
        BackendMetrics.OAuthAttempts.WithLabels("twitch", reason).Inc();
        return Redirect(_publicUrls.GetFrontendReturnUrl("twitch", "error", reason));
    }
}
