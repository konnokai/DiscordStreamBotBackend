using DiscordStreamBotBackend.Model;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Services;

public class DiscordGuildAuthorizationService
{
    private const ulong AdministratorPermission = 8;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public DiscordGuildAuthorizationService(HttpClient httpClient, IMemoryCache cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<List<AdminGuild>> GetManageableGuildsAsync(
        ulong discordUserId,
        string accessToken,
        DateTime sessionIssuedAtUtc,
        DateTime cacheExpiresAtUtc,
        bool useCache,
        CancellationToken cancellationToken)
    {
        var cacheKey = (typeof(DiscordGuildAuthorizationService), discordUserId, sessionIssuedAtUtc);
        if (useCache && _cache.TryGetValue(cacheKey, out List<AdminGuild> cachedGuilds))
            return Clone(cachedGuilds);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me/guilds");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var guilds = JsonConvert.DeserializeObject<List<AdminGuild>>(
            await response.Content.ReadAsStringAsync(cancellationToken));
        if (guilds == null)
            throw new JsonSerializationException("Discord guild 回應沒有內容。");

        var manageableGuilds = guilds.Where(CanManage).ToList();
        if (useCache)
            _cache.Set(cacheKey, manageableGuilds, cacheExpiresAtUtc);

        return Clone(manageableGuilds);
    }

    private static List<AdminGuild> Clone(IEnumerable<AdminGuild> guilds)
        => guilds.Select(guild => new AdminGuild
        {
            Id = guild.Id,
            Name = guild.Name,
            Icon = guild.Icon,
            Owner = guild.Owner,
            Permissions = guild.Permissions
        }).ToList();

    internal static bool CanManage(AdminGuild guild)
    {
        if (guild?.Owner == true)
            return true;

        return ulong.TryParse(guild?.Permissions, NumberStyles.None, CultureInfo.InvariantCulture, out var permissions) &&
            (permissions & AdministratorPermission) == AdministratorPermission;
    }
}
