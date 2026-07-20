using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services.Auth;
using System;

namespace DiscordStreamBotBackend.Services;

public class BearerTokenService
{
    private const string SessionPurpose = "discord_session";
    private const int SessionVersion = 1;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private readonly TokenService _tokenService;

    public BearerTokenService(TokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public string CreateDiscordSessionToken(ulong discordUserId)
    {
        var now = DateTime.UtcNow;
        return _tokenService.CreateToken(new DiscordSessionPayload
        {
            DiscordUserId = discordUserId,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(SessionLifetime),
            Purpose = SessionPurpose,
            Version = SessionVersion
        });
    }

    public bool TryGetDiscordUserId(string authorization, out ulong discordUserId)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            discordUserId = 0;
            return false;
        }

        return TryGetDiscordUserIdFromToken(authorization[prefix.Length..].Trim(), out discordUserId);
    }

    public bool TryGetDiscordUserIdFromToken(string token, out ulong discordUserId)
    {
        discordUserId = 0;

        try
        {
            var payload = _tokenService.GetUser<DiscordSessionPayload>(token);
            var now = DateTime.UtcNow;
            if (payload == null || payload.DiscordUserId == 0 || payload.Purpose != SessionPurpose || payload.Version != SessionVersion ||
                payload.IssuedAtUtc == default || payload.ExpiresAtUtc <= now || payload.ExpiresAtUtc <= payload.IssuedAtUtc ||
                payload.IssuedAtUtc > now.AddMinutes(5))
            {
                return false;
            }

            discordUserId = payload.DiscordUserId;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
