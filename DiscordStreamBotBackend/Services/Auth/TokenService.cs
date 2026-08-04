using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Text;

namespace DiscordStreamBotBackend.Services.Auth
{
    public class TokenService
    {
        /// <summary>
        /// 前後端傳輸金鑰
        /// </summary>
        private readonly string _key;
        /// <summary>
        /// Provider token 加解密金鑰
        /// </summary>
        private readonly string _providerTokenEncryptionKey;

        public TokenService(IConfiguration configuration)
        {
            _key = configuration["Token:Frontend"];
            _providerTokenEncryptionKey = configuration["Token:ProviderTokenEncryptionKey"];
        }

        /// <summary>
        /// 產生加密使用者資料
        /// </summary>
        /// <param name="user">尚未加密的使用者資料</param>
        /// <returns>已加密的使用者資料</returns>
        public string CreateToken(object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var iv = Guid.NewGuid().ToString().Replace("-", "")[..16];

            //使用 AES 加密 Payload
            var encrypt = TokenCrypto
                .AESEncrypt(base64, _key[..16], iv);

            //取得簽章
            var signature = TokenCrypto
                .ComputeHMACSHA256(iv + "." + encrypt, _key[..64]);

            return iv + "." + encrypt + "." + signature;
        }

        /// <summary>
        /// 產生加密使用者資料
        /// </summary>
        /// <param name="user">尚未加密的使用者資料</param>
        /// <returns>已加密的使用者資料</returns>
        public string CreateTokenResponseToken(object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var iv = Guid.NewGuid().ToString().Replace("-", "")[..16];

            //使用 AES 加密 Payload
            var encrypt = TokenCrypto
                .AESEncrypt(base64, _providerTokenEncryptionKey[..16], iv);

            //取得簽章
            var signature = TokenCrypto
                .ComputeHMACSHA256(iv + "." + encrypt, _providerTokenEncryptionKey[..64]);

            return iv + "." + encrypt + "." + signature;
        }

        /// <summary>
        /// 解密使用者資料
        /// </summary>
        /// <param name="token">已加密的使用者資料</param>
        /// <returns>未加密的使用者資料</returns>
        public T GetUser<T>(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return default;

            token = token.Replace(" ", "+");
            var split = token.Split('.');
            if (split.Length != 3) return default;

            var iv = split[0];
            var encrypt = split[1];
            var signature = split[2];

            //檢查簽章是否正確
            if (signature != TokenCrypto.ComputeHMACSHA256(iv + "." + encrypt, _key[..64]))
                return default;

            //使用 AES 解密 Payload
            var base64 = TokenCrypto.AESDecrypt(encrypt, _key[..16], iv);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var payload = JsonConvert.DeserializeObject<T>(json);

            return payload;
        }

        /// <summary>
        /// 解密 User Token Response 資料
        /// </summary>
        /// <param name="token">已加密的 User Token Response 資料</param>
        /// <returns>未加密的 User Token Response 資料</returns>
        /// <exception cref="ArgumentOutOfRangeException">Token 格式錯誤</exception>
        /// <exception cref="ArgumentException">簽章驗證失敗</exception>
        public T GetTokenResponseValue<T>(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return default;

            token = token.Replace(" ", "+");
            var split = token.Split('.');
            if (split.Length != 3) throw new ArgumentOutOfRangeException(nameof(token));

            var iv = split[0];
            var encrypt = split[1];
            var signature = split[2];

            if (signature != TokenCrypto.ComputeHMACSHA256(iv + "." + encrypt, _providerTokenEncryptionKey[..64]))
                throw new ArgumentException("signature");

            var base64 = TokenCrypto.AESDecrypt(encrypt, _providerTokenEncryptionKey[..16], iv);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var payload = JsonConvert.DeserializeObject<T>(json);

            return payload;
        }
    }
}
