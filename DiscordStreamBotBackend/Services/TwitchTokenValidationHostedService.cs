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
        await RunValidationLoopAsync(stoppingToken);
    }

    /// <summary>先停止接受新的 refresh，再停止一般排程，最後等待已接受的 rotation 全部安全寫入。</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 先停止接受新的 refresh，再取消一般驗證迴圈；Twitch 已接受的 rotation 不可因服務關閉而遺失。
        var drainTask = _twitchAuthorizationService.StopAcceptingAndDrainAsync();
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            await drainTask;
        }
    }

    private async Task RunValidationLoopAsync(CancellationToken cancellationToken)
    {
        await ValidateAsync(cancellationToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await ValidateAsync(cancellationToken);
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
            _logger.LogWarning(ex, "Google OAuth 帳號指標更新失敗");
        }
    }
}
