using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Text;

namespace DiscordStreamBotBackend.Services.Auth
{
    public class TokenService
    {
        /// <summary>
        /// 前端工作階段 Token 的加密金鑰。
        /// </summary>
        private readonly string _key;
        /// <summary>
        /// Provider token 的加密與解密金鑰。
        /// </summary>
        private readonly string _providerTokenEncryptionKey;

        public TokenService(IConfiguration configuration)
        {
            _key = configuration["Token:Frontend"];
            _providerTokenEncryptionKey = configuration["Token:ProviderTokenEncryptionKey"];
        }

        /// <summary>
        /// 建立給前端使用的加密 Token。
        /// </summary>
        /// <param name="data">要加密的資料。</param>
        /// <returns>加密後的 Token。</returns>
        public string CreateToken(object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var iv = Guid.NewGuid().ToString().Replace("-", "")[..16];

            // 使用 AES 加密 payload。
            var encrypt = TokenCrypto
                .AESEncrypt(base64, _key[..16], iv);

            // 計算簽章。
            var signature = TokenCrypto
                .ComputeHMACSHA256(iv + "." + encrypt, _key[..64]);

            return iv + "." + encrypt + "." + signature;
        }

        /// <summary>
        /// 建立 provider token 的加密資料。
        /// </summary>
        /// <param name="data">要加密的資料。</param>
        /// <returns>加密後的 provider token。</returns>
        public string CreateTokenResponseToken(object data)
        {
            var json = JsonConvert.SerializeObject(data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var iv = Guid.NewGuid().ToString().Replace("-", "")[..16];

            // 使用 AES 加密 payload。
            var encrypt = TokenCrypto
                .AESEncrypt(base64, _providerTokenEncryptionKey[..16], iv);

            // 計算簽章。
            var signature = TokenCrypto
                .ComputeHMACSHA256(iv + "." + encrypt, _providerTokenEncryptionKey[..64]);

            return iv + "." + encrypt + "." + signature;
        }

        /// <summary>
        /// 解密前端 Token，還原使用者資料。
        /// </summary>
        /// <param name="token">加密後的 Token。</param>
        /// <returns>解密後的使用者資料。</returns>
        public T GetUser<T>(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return default;

            token = token.Replace(" ", "+");
            var split = token.Split('.');
            if (split.Length != 3) return default;

            var iv = split[0];
            var encrypt = split[1];
            var signature = split[2];

            // 確認簽章是否正確。
            if (signature != TokenCrypto.ComputeHMACSHA256(iv + "." + encrypt, _key[..64]))
                return default;

            // 使用 AES 解密 payload。
            var base64 = TokenCrypto.AESDecrypt(encrypt, _key[..16], iv);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var payload = JsonConvert.DeserializeObject<T>(json);

            return payload;
        }

        /// <summary>
        /// 解密 provider token。
        /// </summary>
        /// <param name="token">加密後的 provider token。</param>
        /// <returns>解密後的 provider token。</returns>
        /// <exception cref="ArgumentOutOfRangeException">Token 格式不正確。</exception>
        /// <exception cref="ArgumentException">簽章驗證失敗。</exception>
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
                throw new ArgumentException("Token 簽章驗證失敗。");

            var base64 = TokenCrypto.AESDecrypt(encrypt, _providerTokenEncryptionKey[..16], iv);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var payload = JsonConvert.DeserializeObject<T>(json);

            return payload;
        }
    }
}
