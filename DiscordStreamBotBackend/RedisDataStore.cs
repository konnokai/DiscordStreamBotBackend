using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.Services.Auth;
using Google.Apis.Util.Store;
using Newtonsoft.Json;
using NLog;
using NLog.Web;
using System;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend
{
    public class RedisDataStore : ITokenDataStore
    {
        private readonly RedisService _redisService;
        private readonly TokenService _tokenService;
        private readonly Logger _logger = LogManager.Setup().LoadConfigurationFromAppSettings(AppContext.BaseDirectory).GetCurrentClassLogger();

        public RedisDataStore(RedisService redisService, TokenService tokenService)
        {
            _redisService = redisService;
            _tokenService = tokenService;
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync<T>(string key)
        {
            await _redisService.RedisDb.KeyDeleteAsync(GenerateStoredKey(key, typeof(T)));
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (!await _redisService.RedisDb.KeyExistsAsync(GenerateStoredKey(key, typeof(T))))
                return default(T);

            var str = (await _redisService.RedisDb.StringGetAsync(GenerateStoredKey(key, typeof(T)))).ToString();

            try
            {
                return _tokenService.GetTokenResponseValue<T>(str);
            }
            catch (Exception ex)
            {
                _logger.Warn($"RedisDataStore-GetAsync ({key}): 解密失敗，也許還沒加密? {ex}");

                try
                {
                    return JsonConvert.DeserializeObject<T>(str);
                }
                catch (Exception ex2)
                {
                    _logger.Error($"RedisDataStore-GetAsync ({key}): JsonDes 失敗 {ex2}");
                    return default(T);
                }
            }
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            var encValue = _tokenService.CreateTokenResponseToken(value);
            await _redisService.RedisDb.StringSetAsync(GenerateStoredKey(key, typeof(T)), encValue);
        }

        public async Task<bool> IsExistUserTokenAsync<T>(string key)
        {
            return await _redisService.RedisDb.KeyExistsAsync(GenerateStoredKey(key, typeof(T)));
        }

        public static string GenerateStoredKey(string key, Type t)
        {
            return string.Format("{0}:{1}", t.FullName, key);
        }
    }
}
