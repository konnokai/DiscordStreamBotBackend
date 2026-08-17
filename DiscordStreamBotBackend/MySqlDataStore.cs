using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.DataBase.Table;
using DiscordStreamBotBackend.Services.Auth;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NLog;
using NLog.Web;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend
{
    internal enum ProviderTokenLoadStatus
    {
        Missing,
        Loaded,
        Unreadable
    }

    internal readonly record struct ProviderTokenLoadResult<T>(
        ProviderTokenLoadStatus Status,
        T Value,
        string EncryptedPayload,
        Exception Error);

    internal sealed class ProviderTokenUnreadableException : Exception
    {
        public ProviderTokenUnreadableException(string key, Exception innerException)
            : base($"Provider token '{key}' 已存在，但無法讀取。", innerException)
        {
        }
    }

    /// <summary>
    /// 會限 OAuth token 的 MySQL 儲存後端，也是 token 的資料來源。
    /// T 一律是 Google.Apis 的 TokenResponse，key 是 Discord userId 字串；密文格式與 Bot 共用，兩端都能解密。
    /// </summary>
    public class MySqlDataStore : IDataStore
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
            var result = await LoadAsync<T>(key, CancellationToken.None);
            return result.Status switch
            {
                ProviderTokenLoadStatus.Missing => default,
                ProviderTokenLoadStatus.Loaded => result.Value,
                _ => throw new ProviderTokenUnreadableException(key, result.Error)
            };
        }

        internal async Task<bool> HasUnlinkIntentAsync(
            ulong discordUserId,
            CancellationToken cancellationToken)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return await db.GoogleOAuthUnlinkIntent.AsNoTracking()
                .AnyAsync(x => x.DiscordUserId == discordUserId, cancellationToken);
        }

        internal async Task<bool> StoreAuthorizationIfNoUnlinkIntentAsync<T>(
            ulong discordUserId,
            T value,
            CancellationToken cancellationToken)
        {
            var encryptedValue = _tokenService.CreateTokenResponseToken(value);
            var dateAdded = DateTime.UtcNow;
            using var db = _dbContextFactory.CreateDbContext();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            if (await db.GoogleOAuthUnlinkIntent.AsNoTracking()
                .AnyAsync(x => x.DiscordUserId == discordUserId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO `youtube_member_access_token`
    (`discord_user_id`, `encrypted_access_token`, `date_added`)
VALUES ({discordUserId}, {encryptedValue}, {dateAdded})
ON DUPLICATE KEY UPDATE
    `encrypted_access_token` = {encryptedValue},
    `date_added` = {dateAdded}", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        internal async Task<bool> StoreRefreshIfCurrentAsync<T>(
            ulong discordUserId,
            string expectedEncryptedToken,
            T value,
            CancellationToken cancellationToken)
        {
            var encryptedValue = _tokenService.CreateTokenResponseToken(value);
            var dateAdded = DateTime.UtcNow;
            using var db = _dbContextFactory.CreateDbContext();
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE `youtube_member_access_token`
SET `encrypted_access_token` = {encryptedValue},
    `date_added` = {dateAdded}
WHERE `discord_user_id` = {discordUserId}
  AND BINARY `encrypted_access_token` = BINARY {expectedEncryptedToken}
  AND NOT EXISTS (
      SELECT 1 FROM `google_oauth_unlink_intent`
      WHERE `discord_user_id` = {discordUserId})", cancellationToken);
            return updated == 1;
        }

        internal async Task<ProviderTokenLoadResult<T>> LoadAsync<T>(
            string key,
            CancellationToken cancellationToken)
        {
            var userId = ulong.Parse(key);

            using var db = _dbContextFactory.CreateDbContext();
            var encryptedPayload = await db.YoutubeMemberAccessToken.AsNoTracking()
                .Where(x => x.DiscordUserId == userId)
                .Select(x => x.EncryptedAccessToken)
                .FirstOrDefaultAsync(cancellationToken);
            var result = DecodeStoredToken<T>(encryptedPayload, _tokenService);

            if (result.Status == ProviderTokenLoadStatus.Unreadable)
                _logger.Error(result.Error, $"MySqlDataStore-LoadAsync ({key}): token 解密或反序列化失敗。");

            return result;
        }

        internal static ProviderTokenLoadResult<T> DecodeStoredToken<T>(
            string encryptedPayload,
            TokenService tokenService)
        {
            if (encryptedPayload == null)
                return new ProviderTokenLoadResult<T>(ProviderTokenLoadStatus.Missing, default, null, null);

            Exception decryptError;
            try
            {
                var value = tokenService.GetTokenResponseValue<T>(encryptedPayload);
                if (value is not null)
                    return new ProviderTokenLoadResult<T>(ProviderTokenLoadStatus.Loaded, value, encryptedPayload, null);

                decryptError = new JsonSerializationException("解密後的 provider token payload 為空。");
            }
            catch (Exception ex)
            {
                decryptError = ex;
            }

            try
            {
                // 舊資料曾以未加密 JSON 儲存；只在 AES/HMAC 解析失敗後保留這條相容路徑。
                var value = JsonConvert.DeserializeObject<T>(encryptedPayload);
                if (value is not null)
                    return new ProviderTokenLoadResult<T>(ProviderTokenLoadStatus.Loaded, value, encryptedPayload, null);

                throw new JsonSerializationException("舊格式的 provider token payload 為空。");
            }
            catch (Exception jsonError)
            {
                return new ProviderTokenLoadResult<T>(
                    ProviderTokenLoadStatus.Unreadable,
                    default,
                    encryptedPayload,
                    new AggregateException(decryptError, jsonError));
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
    }
}
