using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers
{
    [Route("[action]")]
    [ApiController]
    public class RandomVideoController : Controller
    {
        private readonly ILogger<RandomVideoController> _logger;
        private readonly RedisService _redisService;
        private readonly PublicUrlService _publicUrls;

        public RandomVideoController(ILogger<RandomVideoController> logger, RedisService redisService, PublicUrlService publicUrls)
        {
            _logger = logger;
            _redisService = redisService;
            _publicUrls = publicUrls;
        }

        [EnableCors("allowGET")]
        [HttpGet]
        public async Task<RedirectResult> RandomVideo()
        {
            try
            {
                await _redisService.RedisDb.StringIncrementAsync("discord_stream_bot:randomVideoClickCount");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis Increment Error\n");
            }

            try
            {
                List<string> randomVideoUrlList = new()
                {
                    "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
                    "https://www.youtube.com/watch?v=ST-Q-hX9Yzo",
                    "https://www.youtube.com/watch?v=h-mUGj41hWA",
                    "https://www.youtube.com/watch?v=BMvqvnyGtGo",
                    "https://www.youtube.com/watch?v=0rLGxUxucdE",
                    "https://www.youtube.com/watch?v=Z_VNp7VUtqA",
                    "https://www.youtube.com/watch?v=uSvGR5H7lUk"
                };

                if (System.IO.File.Exists("RandomVideoUrl.txt"))
                {
                    string[] strings = System.IO.File.ReadAllLines("RandomVideoUrl.txt");
                    if (strings.Any())
                        randomVideoUrlList.AddRange(strings.Where((x) => !string.IsNullOrWhiteSpace(x)).Select((x) => x.Trim()));
                }

                if (_redisService.NowRecordList.Any())
                    randomVideoUrlList.AddRange(_redisService.NowRecordList.Select((x) => $"https://www.youtube.com/watch?v={x}"));

                var index = RNG.Next(randomVideoUrlList.Count);
                _logger.LogInformation("randomVideoUrlList.Count: {randomVideoUrlList.Count}, RNG.Next: {index}", randomVideoUrlList.Count, index);

                string randomUrl = randomVideoUrlList[Math.Max(0, Math.Min(randomVideoUrlList.Count - 1, index))];
                _logger.LogInformation(randomUrl);

                return Redirect(randomUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RandomVideo\n");
                return Redirect(_publicUrls.FrontendDomain);
            }
        }
    }
}
