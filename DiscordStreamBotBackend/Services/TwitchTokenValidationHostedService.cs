using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class TwitchTokenValidationHostedService : BackgroundService
{
    private readonly ILogger<TwitchTokenValidationHostedService> _logger;
    private readonly GoogleOAuthService _googleOAuthService;
    private readonly TwitchAuthorizationService _twitchAuthorizationService;

    public TwitchTokenValidationHostedService(
        ILogger<TwitchTokenValidationHostedService> logger,
        GoogleOAuthService googleOAuthService,
        TwitchAuthorizationService twitchAuthorizationService)
    {
        _logger = logger;
        _googleOAuthService = googleOAuthService;
        _twitchAuthorizationService = twitchAuthorizationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ValidateAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ValidateAsync(stoppingToken);
    }

    private async Task ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _twitchAuthorizationService.ValidateAllAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twitch token 定期驗證失敗");
        }

        try
        {
            await _googleOAuthService.UpdateMetricsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google OAuth 帳號 metrics 更新失敗");
        }
    }
}
