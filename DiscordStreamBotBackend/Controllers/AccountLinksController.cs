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
    private readonly GoogleAccountLinkService _googleAccountLinkService;
    private readonly TwitchAuthorizationService _twitchAuthorizationService;

    public AccountLinksController(
        BearerTokenService bearerTokenService,
        GoogleAccountLinkService googleAccountLinkService,
        TwitchAuthorizationService twitchAuthorizationService)
    {
        _bearerTokenService = bearerTokenService;
        _googleAccountLinkService = googleAccountLinkService;
        _twitchAuthorizationService = twitchAuthorizationService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        var googleTask = _googleAccountLinkService.GetAccountLinkAsync(discordUserId, cancellationToken);
        var twitchTask = _twitchAuthorizationService.GetAccountLinkAsync(discordUserId, cancellationToken);
        await Task.WhenAll(googleTask, twitchTask);
        return Ok(new { google = await googleTask, twitch = await twitchTask });
    }

    [HttpDelete("google")]
    public async Task<IActionResult> DeleteGoogle(CancellationToken cancellationToken)
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        var result = await _googleAccountLinkService.UnlinkAsync(discordUserId, cancellationToken);
        return CreateGoogleUnlinkResult(result);
    }

    [HttpDelete("twitch")]
    public async Task<IActionResult> DeleteTwitch()
    {
        if (!TryGetDiscordUserId(out var discordUserId))
            return Unauthorized(new { error = "Discord 登入憑證無效，請重新登入。" });

        var result = await _twitchAuthorizationService.UnlinkAsync(discordUserId, CancellationToken.None);
        if (result == TwitchUnlinkResult.RevocationPending)
            return Accepted(new { status = "revocation_pending", message = "Twitch 連結撤銷暫時失敗，稍後會自動重試。" });

        return Ok(new { status = "revoked", message = "Twitch 連結已解除。" });
    }

    private bool TryGetDiscordUserId(out ulong discordUserId)
        => _bearerTokenService.TryGetDiscordUserId(Request.Headers.Authorization.ToString(), out discordUserId);

    internal static IActionResult CreateGoogleUnlinkResult(GoogleUnlinkResult result)
    {
        if (result == GoogleUnlinkResult.ProviderRevokeFailed)
            return new ObjectResult(new { error = "google_revoke_failed" }) { StatusCode = 503 };
        if (result == GoogleUnlinkResult.TokenChanged)
            return new ConflictObjectResult(new { error = "google_token_changed", retryable = true });

        var response = new Model.GoogleUnlinkResponse
        {
            CleanupPending = result == GoogleUnlinkResult.CleanupPending
        };
        return response.CleanupPending
            ? new ObjectResult(response) { StatusCode = 202 }
            : new OkObjectResult(response);
    }
}
