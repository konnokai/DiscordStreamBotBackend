using DiscordStreamBotBackend.Model.TwitCasting;
using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.IO;

namespace DiscordStreamBotBackend.Controllers
{
    [Route("[action]")]
    [ApiController]
    public class TwitCastingWebHookController : Controller
    {
        private readonly ILogger<TwitCastingWebHookController> _logger;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;

        public TwitCastingWebHookController(ILogger<TwitCastingWebHookController> logger, IConfiguration configuration, RedisService redisService)
        {
            _logger = logger;
            _configuration = configuration;
            _redisService = redisService;

            if (string.IsNullOrEmpty(_configuration["TwitCasting:WebHookSignature"]))
            {
                _logger.LogError("未設定 TwitCasting Webhook 簽章，請在 appsettings.json 或環境變數中設定。");
            }
        }

        [HttpPost]
        public ContentResult TwitCastingWebHook()
        {
            try
            {
                var content = new StreamReader(Request.Body).ReadToEnd();
                var webHookJson = JsonConvert.DeserializeObject<TwitCastingWebHookJson>(content);

                if (webHookJson == null)
                {
                    _logger.LogError("TwitCasting Webhook 內容為空。");
                    return new ContentResult { StatusCode = 400 };
                }

                if (webHookJson.Signature != _configuration["TwitCasting:WebHookSignature"])
                {
                    _logger.LogError("TwitCasting Webhook 簽章無效。");
                    return new ContentResult { StatusCode = 401 };
                }

                _logger.LogInformation("收到 TwitCasting Webhook 資料：（Live：{IsLive}）{ChannelName} - {Title}",
                    webHookJson.Movie.IsLive, webHookJson.Broadcaster.Name, webHookJson.Movie.Title);

                if (webHookJson.Movie.IsLive)
                {
                    _redisService.AddPubMessage("twitcasting.pubsub.startlive", content);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "讀取 TwitCasting Webhook 資料失敗。");
                return new ContentResult { StatusCode = 500 };
            }

            return new ContentResult { StatusCode = 204 };
        }
    }
}
