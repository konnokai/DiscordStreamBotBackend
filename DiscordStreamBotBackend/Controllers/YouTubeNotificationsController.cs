using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DiscordStreamBotBackend.Controllers
{
    // https://www.codeproject.com/Tips/1229912/Push-Notification-PubSubHubBub-from-Youtube-to-Csh
    [Route("[action]")]
    [ApiController]
    public class YouTubeNotificationsController : ControllerBase
    {
        private readonly ILogger<YouTubeNotificationsController> _logger;
        private readonly RedisService _redisService;
        private readonly MainDbContext _db;

        public YouTubeNotificationsController(ILogger<YouTubeNotificationsController> logger, RedisService redisService, MainDbContext db)
        {
            _logger = logger;
            _redisService = redisService;
            _db = db;
        }

        [HttpGet]
        [HttpPost]
        public ContentResult NotificationCallback([FromQuery(Name = "hub.topic")] string topic,
            [FromQuery(Name = "hub.challenge")] string challenge,
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.verify_token")] string verifyToken,
            [FromQuery(Name = "hub.lease_seconds")] string leaseSeconds)
        {
            try
            {
                if (!Request.Headers.TryGetValue("User-Agent", out var userAgent) || !userAgent.ToString().StartsWith("FeedFetcher-Google;"))
                {
                    _logger.LogWarning("缺少 User-Agent 或標頭無效，略過這次請求。");
                    return new ContentResult { StatusCode = 400 };
                }

                if (Request.Method == "POST")
                {
                    try
                    {
                        if (!Request.Headers.TryGetValue("X-Hub-Signature", out var signature) || !signature.ToString().Contains('='))
                        {
                            _logger.LogWarning("缺少 X-Hub-Signature 或標頭無效，略過這次請求。");
                            return new ContentResult { StatusCode = 400 };
                        }

                        if (!Request.Headers.TryGetValue("Content-Type", out var contentType) || contentType != "application/atom+xml")
                        {
                            _logger.LogWarning("缺少 Content-Type 或標頭無效，略過這次請求。");
                            return new ContentResult { StatusCode = 400 };
                        }

                        var data = ConvertAtomToClass(Request.Body, signature.ToString().Split(new char[] { '=' })[1]);
                        if (data == null)
                            return new ContentResult { StatusCode = 400 };

                        _logger.LogInformation("{Data}", data.ToString());
                        return new ContentResult { StatusCode = 200 };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "NotificationCallback 解析 Atom 失敗。");
                        return new ContentResult { StatusCode = 500 };
                    }
                }
                else if (Request.Method == "GET")
                {
                    _logger.LogInformation("收到 YouTube PubSub callback。topic: {topic} | challenge: {challenge} | mode: {mode} | verifyToken: {verifyToken} | leaseSeconds: {leaseSeconds}",
                        topic, challenge, mode, verifyToken, leaseSeconds);

                    string channelId = new Regex(@"channel_id=(?'ChannelId'[\w\-\\_]{24})").Match(topic).Groups["ChannelId"].Value;

                    switch (mode)
                    {
                        case "subscribe":
                            if (!string.IsNullOrEmpty(verifyToken))
                            {
                                try
                                {
                                    if (string.IsNullOrEmpty(channelId))
                                        return new ContentResult { StatusCode = 400 };

                                    if (!_db.YoutubeChannelSpider.Any((x) => x.ChannelId == channelId))
                                        return new ContentResult { StatusCode = 404 };

                                    _redisService.RedisDb.StringSet($"youtube.pubsub.HMACSecret:{channelId}", verifyToken, TimeSpan.FromSeconds(int.Parse(leaseSeconds)));

                                    _db.YoutubeChannelSpider.Find(channelId).LastSubscribeTime = DateTime.Now;
                                    _db.SaveChanges();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "設定 VerifyToken 失敗。");
                                    return new ContentResult { StatusCode = 500 };
                                }
                            }
                            return Content(challenge);
                        case "unsubscribe":
                            try
                            {
                                _redisService.RedisDb.StringGetDelete($"youtube.pubsub.HMACSecret:{channelId}");
                                return Content(challenge);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "取消訂閱失敗。");
                                return new ContentResult { StatusCode = 500 };
                            }
                        default:
                            _logger.LogWarning("NotificationCallback 收到未知的 mode：{Mode}", mode);
                            return new ContentResult { StatusCode = 400 };
                    }
                }
                else
                {
                    _logger.LogWarning("NotificationCallback 收到不支援的 HTTP method。");
                    return new ContentResult { StatusCode = 400 };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotificationCallback 處理失敗。");
                return new ContentResult { StatusCode = 500 };
            }
        }

        //https://ithelp.ithome.com.tw/articles/10186605
        private YoutubePubSubNotification ConvertAtomToClass(Stream stream, string signature)
        {
            try
            {
                var youtubeNotification = new YoutubePubSubNotification();
                string xmlText = new StreamReader(stream).ReadToEnd();
                XmlDocument doc = new();
                doc.LoadXml(xmlText);

                if (xmlText.Contains("https://www.youtube.com/xml/feeds/videos.xml?channel_id"))
                {
                    youtubeNotification.VideoId = doc.GetElementsByTagName("yt:videoId")[0]?.InnerText;
                    youtubeNotification.ChannelId = doc.GetElementsByTagName("yt:channelId")[0]?.InnerText;
                    youtubeNotification.Title = doc.GetElementsByTagName("title")[1]?.InnerText;
                    youtubeNotification.Link = doc.GetElementsByTagName("link")[0]?.Attributes["href"].Value;
                    youtubeNotification.Published = (DateTime)(doc.GetElementsByTagName("published")[0]?.InnerText.ConvertDateTime());
                    youtubeNotification.Updated = (DateTime)(doc.GetElementsByTagName("updated")[0]?.InnerText.ConvertDateTime());
                    youtubeNotification.NotificationType = YoutubePubSubNotification.YTNotificationType.CreateOrUpdated;
                }
                else if (xmlText.Contains("deleted-entry"))
                {
                    var node = doc.GetElementsByTagName("at:deleted-entry")[0];
                    if (node == null)
                    {
                        _logger.LogWarning("找不到 at:deleted-entry 節點。");
                        return null;
                    }

                    youtubeNotification.VideoId = node.Attributes.GetNamedItem("ref").Value.Split(new char[] { ':' })[2];
                    youtubeNotification.ChannelId = doc.GetElementsByTagName("uri")[0]?.InnerText;
                    youtubeNotification.ChannelId = youtubeNotification.ChannelId.Substring(youtubeNotification.ChannelId.Length - 24, 24);
                    youtubeNotification.Published = (DateTime)node.Attributes.GetNamedItem("when").Value.ConvertDateTime();
                    youtubeNotification.NotificationType = YoutubePubSubNotification.YTNotificationType.Deleted;
                }
                else
                {
                    _logger.LogWarning("收到無法辨識的 Atom 內容。");
                    _logger.LogWarning("{Atom}", xmlText);
                    return null;
                }

                if (!_redisService.RedisDb.KeyExists($"youtube.pubsub.HMACSecret:{youtubeNotification.ChannelId}"))
                {
                    _logger.LogWarning("Redis 找不到 {YoutubeChannelId} 的 HMACSecret。", youtubeNotification.ChannelId);
                    _redisService.AddPubMessage("youtube.pubsub.NeedRegister", youtubeNotification.ChannelId);
                    return null;
                }

                string HMACsecret = _redisService.RedisDb.StringGet($"youtube.pubsub.HMACSecret:{youtubeNotification.ChannelId}");
                string HMACSHA1 = ConvertToHexadecimal(SignWithHmac(xmlText, HMACsecret));
                if (HMACSHA1 != signature)
                {
                    _logger.LogWarning("HMACSHA1 驗證失敗，計算值：{HMACSHA1}，收到值：{Signature}", HMACSHA1, signature);
                    _redisService.AddPubMessage("youtube.pubsub.NeedRegister", youtubeNotification.ChannelId);
                    return null;
                }

                if (youtubeNotification.NotificationType == YoutubePubSubNotification.YTNotificationType.CreateOrUpdated)
                    _redisService.AddYouTubePubMessage(youtubeNotification);

                return youtubeNotification;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析 Atom 失敗。");
                return null;
            }
        }


        //https://stackoverflow.com/questions/4390543/facebook-real-time-update-validating-x-hub-signature-sha1-signature-in-c-sharp
        private static byte[] SignWithHmac(string dataToSign, string keyBody)
        {
            byte[] key = Encoding.UTF8.GetBytes(keyBody);
            byte[] data = Encoding.UTF8.GetBytes(dataToSign);
            using var hmacAlgorithm = new HMACSHA1(key);
            return hmacAlgorithm.ComputeHash(data);
        }

        //https://stackoverflow.com/questions/4390543/facebook-real-time-update-validating-x-hub-signature-sha1-signature-in-c-sharp
        private static string ConvertToHexadecimal(IEnumerable<byte> bytes)
        {
            var builder = new StringBuilder();
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }
    }

    public class YoutubePubSubNotification
    {
        public enum YTNotificationType { CreateOrUpdated, Deleted }

        public YTNotificationType NotificationType { get; set; } = YTNotificationType.CreateOrUpdated;
        public string VideoId { get; set; }
        public string ChannelId { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public DateTime Published { get; set; }
        public DateTime Updated { get; set; }

        public override string ToString()
        {
            switch (NotificationType)
            {
                case YTNotificationType.CreateOrUpdated:
                    return $"({NotificationType} at {Updated}) {ChannelId} - {VideoId} | {Title}";
                case YTNotificationType.Deleted:
                    return $"({NotificationType} at {Published}) {ChannelId} - {VideoId}";
                default:
                    break;
            }
            return "";
        }
    }

    public static class Ext
    {
        public static DateTime? ConvertDateTime(this string text)
        {
            try
            {
                return Convert.ToDateTime(text);
            }
            catch
            {
                return new DateTime();
            }
        }
    }
}
