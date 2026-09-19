using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using NLog;
using StackExchange.Redis;
using System;
using System.Text;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Middleware
{
    public class LogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly RedisService _redisService;
        private Logger logger = LogManager.GetLogger("ACCE");

        public LogMiddleware(RequestDelegate next, RedisService redisService)
        {
            _next = next;
            _redisService = redisService;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var originalResponseBodyStream = context.Response.Body;

            try
            {
                var remoteIpAddress = context.GetRemoteIPAddress();
                var remoteIpText = remoteIpAddress?.ToString() ?? "unknown";
                var requestPath = context.Request.Path.Value ?? "/";
                string badReqRedisKey = $"server.errorcount:{remoteIpText.Replace(":", "-").Replace(".", "-")}";
                string rngReqRedisKey = $"server.rngvideocount:{remoteIpText.Replace(":", "-").Replace(".", "-")}";
                bool isRedisError = false;

                // YouTube WebSub 的 challenge／通知全部來自 Hub 的同一個 IP，一次大量續訂會產生連續的 404；
                // 若把這些算進 bad request，授權正確的 challenge 會被 429 擋在 controller 之前而持續失敗。
                // 該端點本身只做嚴格驗證且快速回應，因此不套用通用的 bad request 節流。
                bool isYoutubeCallback = requestPath.Equals("/NotificationCallback", StringComparison.OrdinalIgnoreCase);

                try
                {
                    // 以 media type 判斷：`application/atom+xml; charset=utf-8` 是合法通知，不可算成 bad request。
                    if (!isYoutubeCallback && !YoutubeWebSub.YoutubeWebSubContract.IsAtomContentType(context.Request.Headers.ContentType.ToString()))
                    {
                        var badCount = await _redisService.RedisDb.StringGetAsync(badReqRedisKey);
                        if (badCount.HasValue && int.Parse(badCount.ToString()) >= 5)
                        {
                            await _redisService.RedisDb.StringIncrementAsync(badReqRedisKey);
                            await _redisService.RedisDb.KeyExpireAsync(badReqRedisKey, TimeSpan.FromHours(1));
                            var errorMessage = JsonConvert.SerializeObject(new
                            {
                                ErrorMessage = "429 請求過於頻繁"
                            });
                            var bytes = Encoding.UTF8.GetBytes(errorMessage);

                            context.Response.StatusCode = 429;
                            await originalResponseBodyStream.WriteAsync(
                                bytes, 0, bytes.Length);
                            return;
                        }
                    }
                    if (requestPath.Contains("randomvideo", StringComparison.OrdinalIgnoreCase))
                    {
                        var rngReqCount = await _redisService.RedisDb.StringGetAsync(rngReqRedisKey);
                        if (rngReqCount.HasValue && int.Parse(rngReqCount.ToString()) >= 5)
                        {
                            await _redisService.RedisDb.StringIncrementAsync(rngReqRedisKey);
                            await _redisService.RedisDb.KeyExpireAsync(rngReqRedisKey, TimeSpan.FromHours(1));
                            var errorMessage = JsonConvert.SerializeObject(new
                            {
                                ErrorMessage = "429 請求過於頻繁"
                            });
                            var bytes = Encoding.UTF8.GetBytes(errorMessage);

                            context.Response.StatusCode = 429;
                            await originalResponseBodyStream.WriteAsync(
                                bytes, 0, bytes.Length);
                            return;
                        }
                    }
                }
                catch (RedisConnectionException redisEx)
                {
                    logger.Error(redisEx, "Redis 連線中斷。");
                    isRedisError = true;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Middleware 處理失敗。");
                }

                await _next(context);

                var route = context.GetRouteValue("action")?.ToString()?.ToLower();
                if (route != null && route == "statuscheck" && context.Response.StatusCode == 200)
                    return;

                logger.Info($"{remoteIpText} | {context.Request.Method} | {context.Response.StatusCode} | {requestPath}");

                if (!isRedisError)
                {
                    if (!isYoutubeCallback && context.Response.StatusCode >= 400 && context.Response.StatusCode < 500)
                    {
                        await _redisService.RedisDb.StringIncrementAsync(badReqRedisKey);
                        await _redisService.RedisDb.KeyExpireAsync(badReqRedisKey, TimeSpan.FromHours(1));
                    }
                    if (requestPath.Contains("randomvideo", StringComparison.OrdinalIgnoreCase))
                    {
                        await _redisService.RedisDb.StringIncrementAsync(rngReqRedisKey);
                        await _redisService.RedisDb.KeyExpireAsync(rngReqRedisKey, TimeSpan.FromHours(1));
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e);

                if (context.Response.HasStarted)
                    throw;

                var errorMessage = JsonConvert.SerializeObject(new
                {
                    ErrorMessage = "伺服器發生錯誤"
                });
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(errorMessage, Encoding.UTF8);
            }
        }
    }
}
