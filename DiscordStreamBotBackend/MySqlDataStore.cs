using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;
using NLog.Web;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend
{
    /// <summary>
    /// 會限 OAuth token 的 MySQL 儲存後端（真實來源）。
    /// T 恆為 Google.Apis 的 TokenResponse、key 為 Discord userId 字串；密文格式與 <see cref="RedisDataStore"/> 相同，與 Bot 端可互相解密。
    /// </summary>
    public class MySqlDataStore : ITokenDataStore
    {
        private readonly IDbContextFactory<MainDbContext> _dbContextFactory;
        private readonly TokenService _tokenService;
        private readonly Logger _logger = LogManager.Setup().LoadConfigurationFromAppSettings(AppContext.BaseDirectory).GetCurrentClassLogger();

        public MySqlDataStore(IDbContextFactory<MainDbContext> dbContextFactory, TokenService tokenService)
        {
            _dbContextFactory = dbContextFactory;
            _tokenService = tokenService;
        }

        public Task ClearAsync()
        {
            throw new NotImplementedException();
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            var userId = ulong.Parse(key);
            var encValue = _tokenService.CreateTokenResponseToken(value);

            using var db = _dbContextFactory.CreateDbContext();
            var entity = await db.YoutubeMemberAccessToken.SingleOrDefaultAsync(x => x.DiscordUserId == userId);
            if (entity == null)
            {
                db.YoutubeMemberAccessToken.Add(new YoutubeMemberAccessToken { DiscordUserId = userId, EncryptedAccessToken = encValue, DateAdded = DateTime.Now });
            }
            else
            {
                entity.EncryptedAccessToken = encValue;
                entity.DateAdded = DateTime.Now;
            }

            await db.SaveChangesAsync();
        }

        public async Task<T> GetAsync<T>(string key)
        {
            var userId = ulong.Parse(key);

            using var db = _dbContextFactory.CreateDbContext();
            var str = await db.YoutubeMemberAccessToken.AsNoTracking()
                .Where(x => x.DiscordUserId == userId)
                .Select(x => x.EncryptedAccessToken)
                .FirstOrDefaultAsync();

            if (str == null)
                return default(T);

            try
            {
                return _tokenService.GetTokenResponseValue<T>(str);
            }
            catch (Exception ex)
            {
                _logger.Warn($"MySqlDataStore-GetAsync ({key}): 解密失敗，也許還沒加密? {ex}");

                try
                {
                    return JsonConvert.DeserializeObject<T>(str);
                }
                catch (Exception ex2)
                {
                    _logger.Error($"MySqlDataStore-GetAsync ({key}): JsonDes 失敗 {ex2}");
                    return default(T);
                }
            }
        }

        public async Task DeleteAsync<T>(string key)
        {
            var userId = ulong.Parse(key);

            using var db = _dbContextFactory.CreateDbContext();
            var entity = await db.YoutubeMemberAccessToken.SingleOrDefaultAsync(x => x.DiscordUserId == userId);
            if (entity != null)
            {
                db.YoutubeMemberAccessToken.Remove(entity);
                await db.SaveChangesAsync();
            }
        }

        public async Task<bool> IsExistUserTokenAsync<T>(string key)
        {
            var userId = ulong.Parse(key);

            using var db = _dbContextFactory.CreateDbContext();
            return await db.YoutubeMemberAccessToken.AsNoTracking().AnyAsync(x => x.DiscordUserId == userId);
        }
    }
}
