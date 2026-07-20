using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Net;
using System.Text;

namespace DiscordStreamBotBackend
{
    public static class Utility
    {
        public static string TwitchWebHookSecret { get; set; }

        private const string UnReservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        /// <summary>
        /// Url Encoding
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string UrlEncode(string value)
        {
            StringBuilder result = new();

            foreach (char symbol in value)
            {
                if (UnReservedChars.Contains(symbol))
                {
                    result.Append(symbol);
                }
                else
                {
                    result.Append('%' + String.Format("{0:X2}", (int)symbol));
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// 取得 ASP.NET Core 連線層正規化後的遠端 IP。
        /// </summary>
        /// <param name="context">Http context</param>
        /// <returns>IPAddress</returns>
        public static IPAddress GetRemoteIPAddress(this HttpContext context) => context.Connection.RemoteIpAddress;
    }

    public class MemberData
    {
        public string DiscordUserId { get; set; }
        public string GoogleAccessToken { get; set; }
        public string GoogleRefrechToken { get; set; }
        public DateTime GoogleExpiresIn { get; set; }
        public string GoogleUserName { get; set; }
        public string GoogleUserAvatar { get; set; }
        public string YoutubeChannelId { get; set; }
    }

}
