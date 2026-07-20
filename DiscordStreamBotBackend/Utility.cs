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
        /// Get remote ip address, optionally allowing for x-forwarded-for header check
        /// </summary>
        /// <param name="context">Http context</param>
        /// <param name="allowForwarded">Whether to allow x-forwarded-for header check</param>
        /// <returns>IPAddress</returns>
        public static IPAddress GetRemoteIPAddress(this HttpContext context, bool allowForwarded = true)
        {
            if (allowForwarded)
            {
                // if you are allowing these forward headers, please ensure you are restricting context.Connection.RemoteIpAddress
                // to cloud flare ips: https://www.cloudflare.com/ips/
                string header = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (header == null)
                    return context.Connection.RemoteIpAddress;

                header = header.Split(',')[0];
                if (IPAddress.TryParse(header, out IPAddress ip))
                {
                    return ip;
                }
            }

            return context.Connection.RemoteIpAddress;
        }
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