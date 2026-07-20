using DiscordStreamBotBackend.Model;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class OAuthStateService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly RedisService _redisService;
    private readonly string _frontendDomain;

    public OAuthStateService(RedisService redisService, IConfiguration configuration)
    {
        _redisService = redisService;
        _frontendDomain = configuration["FrontendDomain"];
    }

    public async Task<string> CreateAsync(string provider, ulong discordUserId)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var state = new OAuthStateData
        {
            DiscordUserId = discordUserId,
            Provider = provider,
            FrontendDomain = _frontendDomain
        };

        await _redisService.RedisDb.StringSetAsync(GetKey(provider, nonce), JsonConvert.SerializeObject(state), StateLifetime);
        return nonce;
    }

    public async Task<OAuthStateData> ConsumeAsync(string provider, string nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
            return null;

        var value = await _redisService.RedisDb.StringGetDeleteAsync(GetKey(provider, nonce));
        if (!value.HasValue)
            return null;

        var state = JsonConvert.DeserializeObject<OAuthStateData>(value.ToString());
        if (state?.Provider != provider || state.FrontendDomain != _frontendDomain)
            return null;

        return state;
    }

    private static string GetKey(string provider, string nonce) => $"oauth:state:{provider}:{nonce}";
}
