using Microsoft.Extensions.Configuration;
using System;

namespace DiscordStreamBotBackend.Services;

public class PublicUrlService
{
    public PublicUrlService(IConfiguration configuration)
    {
        FrontendDomain = ValidateDomain(configuration["FrontendDomain"], "FrontendDomain");
        ApiServerDomain = ValidateDomain(configuration["ApiServerDomain"], "ApiServerDomain");
    }

    public string FrontendDomain { get; }
    public string ApiServerDomain { get; }
    public string DiscordRedirectUrl => $"{FrontendDomain}/";
    public string GoogleCallbackUrl => $"{ApiServerDomain}/oauth/google/callback";
    public string TwitchCallbackUrl => $"{ApiServerDomain}/oauth/twitch/callback";

    public string GetFrontendReturnUrl(string provider, string result, string reason = null)
    {
        var url = $"{FrontendDomain}/?provider={Uri.EscapeDataString(provider)}&result={Uri.EscapeDataString(result)}";
        return string.IsNullOrWhiteSpace(reason) ? url : $"{url}&reason={Uri.EscapeDataString(reason)}";
    }

    private static string ValidateDomain(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.EndsWith('/') || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{name} 必須是沒有結尾斜線的絕對 URI。");

        var isLocalhostHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !isLocalhostHttp)
            throw new InvalidOperationException($"{name} 必須使用 HTTPS；本機開發只允許 HTTP localhost。");

        if (string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException($"{name} 只能包含公開網域，不可包含路徑、查詢字串或片段。");

        return value;
    }
}
