using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.Model;
using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.AspNetCore.Cors;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers
{
    [Route("[action]")]
    [ApiController]
    public class YouTubeMemberController : ControllerBase
    {
        private readonly ILogger<YouTubeMemberController> _logger;
        private readonly HttpClient _httpClient;
        private readonly GoogleAuthorizationCodeFlow flow;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;
        private readonly TokenService _tokenService;

        public YouTubeMemberController(ILogger<YouTubeMemberController> logger,
            HttpClient httpClient,
            IConfiguration configuration,
            RedisService redisService,
            TokenService tokenService,
            IDbContextFactory<MainDbContext> dbContextFactory)
        {
            _logger = logger;
            _httpClient = httpClient;
            _configuration = configuration;
            _redisService = redisService;
            _tokenService = tokenService;

            flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _configuration["Google:ClientId"],
                    ClientSecret = _configuration["Google:ClientSecret"]
                },
                Scopes = ["https://www.googleapis.com/auth/youtube.force-ssl"],
                DataStore = new MySqlDataStore(dbContextFactory, _tokenService)
            });
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<APIResult> DiscordCallBack(string code)
        {
            if (string.IsNullOrEmpty(code))
                return new APIResult(ResultStatusCode.BadRequest, "參數錯誤");

            try
            {
                if (await _redisService.RedisDb.KeyExistsAsync($"discord:code:{code}"))
                    return new APIResult(ResultStatusCode.BadRequest, "請確認是否有插件或軟體導致重複驗證\n如網頁正常顯示資料則無需理會");

                await _redisService.RedisDb.StringSetAsync($"discord:code:{code}", "0", TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DiscordCallBack - Redis設定錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }

            try
            {
                DiscordAccessTokenData tokenData = null;
                try
                {
                    var content = new FormUrlEncodedContent(
                    [
                        new("code", code),
                        new("client_id",  _configuration["Discord:ClientId"]),
                        new("client_secret", _configuration["Discord:ClientSecret"]),
                        new("redirect_uri",  _configuration["RedirectUrl"]),
                        new("grant_type", "authorization_code")
                    ]);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                    var response = await _httpClient.PostAsync("https://discord.com/api/v10/oauth2/token", content);

                    response.EnsureSuccessStatusCode();

                    tokenData = JsonConvert.DeserializeObject<DiscordAccessTokenData>(await response.Content.ReadAsStringAsync());

                    if (tokenData == null || tokenData.access_token == null)
                        return new APIResult(ResultStatusCode.Unauthorized, "認證錯誤，請重新登入 Discord");
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("400"))
                    {
                        _logger.LogWarning("{ExceptionMessage}", ex.ToString());
                        return new APIResult(ResultStatusCode.BadRequest, "請重新登入 Discord");
                    }

                    _logger.LogError(ex, "DiscordCallBack - Discord Token 交換錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }

                DiscordUser discordUser = null;
                try
                {
                    // https://docs.discordnet.dev/guides/bearer_token/bearer_token_guide.html
                    // 想嘗試改成用 DiscordRestClient 來撈資料，但這樣回傳的 User 結構會變動導致前端抓不到正確的資料，暫時作罷

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue($"Bearer", tokenData.access_token);

                    var discordMeJson = await _httpClient.GetStringAsync("https://discord.com/api/v10/users/@me");
                    discordUser = JsonConvert.DeserializeObject<DiscordUser>(discordMeJson);

                    _logger.LogInformation("Discord User OAuth Done: {DiscordUsername} ({DiscordUserid})", discordUser.username, discordUser.id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DiscordCallBack - Discord API 回傳錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }

                string token = "";
                try
                {
                    token = _tokenService.CreateToken(discordUser.id);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "DiscordCallBack - 建立 JWT 錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }

                return new APIResult(ResultStatusCode.OK, new { Token = token, DiscordData = discordUser });
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "DiscordCallBack - 整體錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }

        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<APIResult> GoogleCallBack(string code, string state)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return new APIResult(ResultStatusCode.BadRequest, "參數錯誤");

            try
            {
                if (await _redisService.RedisDb.KeyExistsAsync($"google:code:{code}"))
                    return new APIResult(ResultStatusCode.BadRequest, "請確認是否有插件或軟體導致重複驗證\n如網頁正常顯示資料則無需理會");

                await _redisService.RedisDb.StringSetAsync($"google:code:{code}", "0", TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GoogleCallBack - Redis 設定錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }

            try
            {
                var discordUser = _tokenService.GetUser<string>(state);
                if (string.IsNullOrEmpty(discordUser))
                    return new APIResult(ResultStatusCode.Unauthorized, "Token 無效，請重新登入 Discord");

                var googleUser = await flow.LoadTokenAsync(discordUser, CancellationToken.None);

                var googleToken = await flow.ExchangeCodeForTokenAsync(discordUser, code, _configuration["RedirectUrl"], CancellationToken.None);
                if (googleToken == null)
                    return new APIResult(ResultStatusCode.Unauthorized, "請重新登入 Google 帳號");

                if (string.IsNullOrEmpty(googleToken.AccessToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "Google 授權驗證無效\n請解除應用程式授權後再登入 Google 帳號");

                if (!string.IsNullOrEmpty(googleToken.RefreshToken))
                {
                    return new APIResult(ResultStatusCode.OK);
                }
                else if (googleUser != null && !string.IsNullOrEmpty(googleUser.RefreshToken))
                {
                    googleToken.RefreshToken = googleUser.RefreshToken;
                    await flow.DataStore.StoreAsync(discordUser, googleToken);
                    return new APIResult(ResultStatusCode.OK);
                }
                else
                {
                    return await RevokeGoogleToken(discordUser, "無法刷新 Google 授權\n請重新登入 Google 帳號", ResultStatusCode.Unauthorized);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("invalid_grant"))
                {
                    _logger.LogWarning("偵測到 invalid_grant");
                    return new APIResult(ResultStatusCode.Unauthorized, "Google 授權驗證無效或尚未登入\n請解除應用程式授權後再登入 Google 帳號");
                }
                else
                {
                    _logger.LogError(ex, "GoogleCallBack - 整體錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }
            }
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<APIResult> GetGoogleData(string token = "")
        {
            if (string.IsNullOrEmpty(token))
                return new APIResult(ResultStatusCode.BadRequest, "Token 不可為空");

            try
            {
                var discordUser = _tokenService.GetUser<string>(token);
                if (string.IsNullOrEmpty(discordUser))
                    return new APIResult(ResultStatusCode.Unauthorized, "Token 無效");

                var googleToken = await flow.LoadTokenAsync(discordUser, CancellationToken.None);
                if (googleToken == null)
                    return new APIResult(ResultStatusCode.Unauthorized, "請登入 Google 帳號");

                if (string.IsNullOrEmpty(googleToken.AccessToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "Google 授權驗證無效\n請解除應用程式授權後再登入 Google 帳號");

                if (string.IsNullOrEmpty(googleToken.RefreshToken))
                    return await RevokeGoogleToken(discordUser, "無法刷新 Google 授權\n請重新登入 Google 帳號", ResultStatusCode.Unauthorized);

                try
                {
                    if (googleToken.IssuedUtc.AddSeconds((double)googleToken.ExpiresInSeconds).Subtract(DateTime.UtcNow).TotalSeconds <= 0)
                        googleToken = await flow.RefreshTokenAsync(discordUser, googleToken.RefreshToken, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GetGoogleData - 刷新 Token 錯誤\n");
                    return await RevokeGoogleToken(discordUser, "無法刷新 Google 授權\n請重新登入 Google 帳號", ResultStatusCode.Unauthorized);
                }

                MemberData user = new();
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", googleToken.AccessToken);

                try
                {
                    var youtubeUserData = JsonConvert.DeserializeObject<YoutubeChannelMeJson>(
                        await _httpClient.GetStringAsync($"https://youtube.googleapis.com/youtube/v3/channels?part=id%2Csnippet&mine=true"))
                            .items.First();

                    user.YoutubeChannelId = youtubeUserData.id;
                    user.GoogleUserName = youtubeUserData.snippet.title;
                    user.GoogleUserAvatar = youtubeUserData.snippet.thumbnails.@default.url;
                }
                catch (ArgumentNullException)
                {
                    return await RevokeGoogleToken(discordUser, "請確認此 Google 帳號有 Youtube 頻道", ResultStatusCode.BadRequest);
                }
                catch (WebException ex)
                {
                    if (ex.Message.Contains("403"))
                    {
                        _logger.LogError(ex, $"403錯誤\n");
                        return await RevokeGoogleToken(discordUser, "請重新登入，並在登入 Google 帳號時勾選\n\"查看、編輯及永久刪除您的 YouTube 影片、評價、留言和字幕\"", ResultStatusCode.Unauthorized);
                    }
                    else if (ex.Message.Contains("401"))
                    {
                        _logger.LogError(ex, $"401錯誤\n");
                        return new APIResult(ResultStatusCode.InternalServerError, "請嘗試重新登入 Google 帳號");
                    }
                    else
                    {
                        _logger.LogError(ex, "GetGoogleData - Youtube API回傳錯誤\n");
                        return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                    }
                }
                catch (HttpRequestException httpEx) when (httpEx.Message.Contains("403"))
                {
                    _logger.LogError(httpEx, "GetGoogleData - 403錯誤\n");

                    string resetDay = "今";
                    if (DateTime.Now.Hour >= 16)
                        resetDay = "明";

                    return new APIResult(ResultStatusCode.InternalServerError, $"已綁定但 API 無法回傳訊息，請於{resetDay}日 16:00 後再至 Discord 驗證");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GetGoogleData - 其他錯誤\n");
                    return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
                }

                _logger.LogInformation("Google User OAuth Fetch Done: {GoogleUserName}", user.GoogleUserName);
                return new APIResult(ResultStatusCode.OK, new { UserName = user.GoogleUserName, UserAvatarUrl = user.GoogleUserAvatar });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetGoogleData - 整體錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報");
            }
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<APIResult> UnlinkGoogle(string token = "")
        {
            if (string.IsNullOrEmpty(token))
                return new APIResult(ResultStatusCode.BadRequest, "Token 不可為空");

            var discordUser = _tokenService.GetUser<string>(token);
            if (string.IsNullOrEmpty(discordUser))
                return new APIResult(ResultStatusCode.Unauthorized, "Token 無效，請重新登入 Discord");

            return await RevokeGoogleToken(discordUser);
        }

        private async Task<APIResult> RevokeGoogleToken(string discordUser = "", string resultText = null, ResultStatusCode resultStatusCode = ResultStatusCode.OK)
        {
            if (string.IsNullOrEmpty(discordUser))
                return new APIResult(ResultStatusCode.BadRequest, "參數錯誤");

            try
            {
                _redisService.AddPubMessage("member.revokeToken", discordUser);

                var googleToken = await flow.LoadTokenAsync(discordUser, CancellationToken.None);
                if (googleToken == null)
                    return new APIResult(ResultStatusCode.Unauthorized, "未登入 Google 帳號或已解除綁定");

                if (string.IsNullOrEmpty(googleToken.AccessToken))
                    return new APIResult(ResultStatusCode.Unauthorized, "Google 授權驗證無效\n請手動至 Google 帳號安全性解除應用程式授權");

                string revokeToken = googleToken.RefreshToken ?? googleToken.AccessToken;

                await flow.RevokeTokenAsync(discordUser, revokeToken, CancellationToken.None);

                return new APIResult(resultStatusCode, resultText);
            }
            catch (Exception ex)
            {
                await flow.DeleteTokenAsync(discordUser, CancellationToken.None);
                _logger.LogError(ex, "RevokeGoogleToken - 整體錯誤\n");
                return new APIResult(ResultStatusCode.InternalServerError, "伺服器內部錯誤，請向孤之界回報\n請手動至 Google 帳號安全性解除應用程式授權");
            }
        }
    }
}
