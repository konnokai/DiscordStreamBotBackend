using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers;

[ApiController]
[Route("account-links")]
[EnableCors("frontend")]
public class AccountLinksController : ControllerBase
{
    private readonly BearerTokenService _bearerTokenService;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly TwitchAuthorizationService _twitchAuthorizationService;

    public AccountLinksController(
        BearerTokenService bearerTokenService,
        GoogleOAuthService googleOAuthService,
        TwitchAuthorizationService twitchAuthorizationService)
    {
        _bearerTokenService = bearerTokenService;
        _googleOAuthService = googleOAuthService;
        _twitchAuthorizationService = twitchAuthorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        var googleTask = _googleOAuthService.GetAccountLinkAsync(discordUserId, cancellationToken);
        var twitchTask = _twitchAuthorizationService.GetAccountLinkAsync(discordUserId, cancellationToken);
        await Task.WhenAll(googleTask, twitchTask);
        return Ok(new { google = googleTask.Result, twitch = twitchTask.Result });
    }

    [HttpDelete("google")]
    public async Task<IActionResult> DeleteGoogle()
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        if (!await _googleOAuthService.UnlinkAsync(discordUserId, CancellationToken.None))
            return StatusCode(503, new { error = "google_revoke_failed" });

        return Ok(new { status = "unlinked", message = "Google 連結已解除。" });
    }

    [HttpDelete("twitch")]
    public async Task<IActionResult> DeleteTwitch()
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        var result = await _twitchAuthorizationService.UnlinkAsync(discordUserId, CancellationToken.None);
        if (result == TwitchUnlinkResult.RevocationPending)
            return Accepted(new { status = "revocation_pending", message = "Twitch 撤銷暫時失敗，系統將自動重試。" });

        return Ok(new { status = "revoked", message = "Twitch 連結已解除。" });
    }

    private bool TryGetDiscordUserId(out ulong discordUserId)
        => _bearerTokenService.TryGetDiscordUserId(Request.Headers.Authorization.ToString(), out discordUserId);
}
