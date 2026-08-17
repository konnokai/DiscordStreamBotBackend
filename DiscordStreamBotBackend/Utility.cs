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
        /// 將字串進行 URL 編碼。
        /// </summary>
        /// <param name="value">要編碼的字串。</param>
        /// <returns>URL 編碼後的字串。</returns>
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
        /// 取得用戶端 IP 位址，可選擇是否檢查轉送標頭。
        /// </summary>
        /// <param name="context">HTTP context。</param>
        /// <param name="allowForwarded">是否允許檢查 X-Forwarded-For 標頭。</param>
        /// <returns>用戶端 IP 位址。</returns>
        public static IPAddress GetRemoteIPAddress(this HttpContext context, bool allowForwarded = true)
        {
            if (allowForwarded)
            {
                // 若允許使用轉送標頭，請確認 context.Connection.RemoteIpAddress 只接受 Cloudflare IP。
                // https://www.cloudflare.com/ips/
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
