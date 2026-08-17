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

    public string CreateDiscordSessionToken(ulong discordUserId, string discordAccessToken, int providerExpiresInSeconds)
    {
        if (discordUserId == 0)
            throw new ArgumentOutOfRangeException(nameof(discordUserId));
        if (string.IsNullOrWhiteSpace(discordAccessToken))
            throw new ArgumentException("必須提供 Discord access token。", nameof(discordAccessToken));
        if (providerExpiresInSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(providerExpiresInSeconds));

        var now = DateTime.UtcNow;
        var sessionExpiresAtUtc = now.Add(SessionLifetime);
        var providerExpiresAtUtc = now.AddSeconds(providerExpiresInSeconds);
        return _tokenService.CreateToken(new DiscordSessionPayload
        {
            DiscordUserId = discordUserId,
            DiscordAccessToken = discordAccessToken,
            IssuedAtUtc = now,
            ExpiresAtUtc = providerExpiresAtUtc < sessionExpiresAtUtc ? providerExpiresAtUtc : sessionExpiresAtUtc,
            ProviderExpiresAtUtc = providerExpiresAtUtc,
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

        if (!TryGetDiscordSessionPayload(token, out var payload))
            return false;

        discordUserId = payload.DiscordUserId;
        return true;
    }

    public bool TryGetDiscordSession(string authorization, out DiscordSessionPayload session)
    {
        const string prefix = "Bearer ";
        session = null;
        if (string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !TryGetDiscordSessionPayload(authorization[prefix.Length..].Trim(), out var payload))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(payload.DiscordAccessToken) || payload.ProviderExpiresAtUtc <= now ||
            payload.ExpiresAtUtc > payload.ProviderExpiresAtUtc)
        {
            return false;
        }

        session = payload;
        return true;
    }

    private bool TryGetDiscordSessionPayload(string token, out DiscordSessionPayload payload)
    {
        payload = null;

        try
        {
            payload = _tokenService.GetUser<DiscordSessionPayload>(token);
            var now = DateTime.UtcNow;
            if (payload == null || payload.DiscordUserId == 0 || payload.Purpose != SessionPurpose || payload.Version != SessionVersion ||
                payload.IssuedAtUtc == default || payload.ExpiresAtUtc <= now || payload.ExpiresAtUtc <= payload.IssuedAtUtc ||
                payload.IssuedAtUtc > now.AddMinutes(5))
            {
                return false;
            }

            return true;
        }
        catch
        {
            payload = null;
            return false;
        }
    }
}
