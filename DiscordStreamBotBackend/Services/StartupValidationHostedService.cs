using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class StartupValidationHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly RedisService _redisService;

    public StartupValidationHostedService(
        IConfiguration configuration,
        RedisService redisService)
    {
        _configuration = configuration;
        _redisService = redisService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateConfiguration(_configuration);

        var configuredSecret = _configuration["Twitch:WebHookSecret"];
        if (IsMissingOrPlaceholder(configuredSecret))
            throw new InvalidOperationException("Twitch:WebHookSecret 不可為空或 placeholder。");

        var redisSecret = await _redisService.Redis.GetDatabase(0).StringGetAsync("twitch:webhook_secret");
        if (!redisSecret.HasValue || !FixedTimeEquals(configuredSecret, redisSecret.ToString()))
            throw new InvalidOperationException("Twitch WebHook secret 與 Redis DB 0 的 twitch:webhook_secret 不一致。");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static void ValidateConfiguration(IConfiguration configuration)
    {
        ValidateMinimumLength(configuration, "Token:Frontend", 64);
        ValidateMinimumLength(configuration, "Token:ProviderTokenEncryptionKey", 64);
        ValidateRequired(configuration, "FrontendDomain");
        ValidateRequired(configuration, "ApiServerDomain");
        ValidateRequired(configuration, "Discord:ClientId");
        ValidateRequired(configuration, "Discord:ClientSecret");
        ValidateRequired(configuration, "Google:ClientId");
        ValidateRequired(configuration, "Google:ClientSecret");
        ValidateRequired(configuration, "Twitch:ClientId");
        ValidateRequired(configuration, "Twitch:ClientSecret");
        ValidateConnectionString(configuration, "MySql");
        ValidateConnectionString(configuration, "Redis");
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static void ValidateMinimumLength(IConfiguration configuration, string key, int minimumLength)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength || IsPlaceholder(value))
            throw new InvalidOperationException($"{key} 必須至少 {minimumLength} 個字元，且不可使用 placeholder。");
    }

    private static void ValidateRequired(IConfiguration configuration, string key)
    {
        if (IsMissingOrPlaceholder(configuration[key]))
            throw new InvalidOperationException($"{key} 不可為空或 placeholder。");
    }

    private static void ValidateConnectionString(IConfiguration configuration, string name)
    {
        var value = configuration.GetConnectionString(name);
        if (IsMissingOrPlaceholder(value))
            throw new InvalidOperationException($"ConnectionStrings:{name} 不可為空或 placeholder。");
    }

    private static bool IsMissingOrPlaceholder(string value) => string.IsNullOrWhiteSpace(value) || IsPlaceholder(value);

    private static bool IsPlaceholder(string value)
    {
        return value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ch@nge_Me", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("example.com", StringComparison.OrdinalIgnoreCase);
    }
}
