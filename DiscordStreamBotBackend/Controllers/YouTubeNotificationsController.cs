using DiscordStreamBotBackend.DataBase;
using DiscordStreamBotBackend.Services;
using DiscordStreamBotBackend.YoutubeWebSub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordStreamBotBackend.Controllers
{
    /// <summary>
    /// YouTube PubSubHubbub（WebSub 0.4）callback：challenge／denied 的訂閱意圖驗證，以及通知 POST 的
    /// raw-body HMAC 驗證與 Redis 轉發。決策邏輯集中在 <see cref="YoutubeWebSubCallbackPolicy"/>。
    /// <para>
    /// 授權完全依 pending action、topic、channelId 與 callback token／HMAC 完成，不以 User-Agent 判斷。
    /// Hub 不可能重試成功的無效通知一律忽略並快速回 2xx，避免反覆重送。
    /// </para>
    /// </summary>
    // https://www.codeproject.com/Tips/1229912/Push-Notification-PubSubHubBub-from-Youtube-to-Csh
    [Route("[action]")]
    [ApiController]
    public class YouTubeNotificationsController : ControllerBase
    {
        private readonly ILogger<YouTubeNotificationsController> _logger;
        private readonly RedisService _redisService;
        private readonly YoutubeWebSubStateStore _state;
        private readonly MainDbContext _db;

        public YouTubeNotificationsController(
            ILogger<YouTubeNotificationsController> logger,
            RedisService redisService,
            YoutubeWebSubStateStore state,
            MainDbContext db)
        {
            _logger = logger;
            _redisService = redisService;
            _state = state;
            _db = db;
        }

        [HttpGet]
        [HttpPost]
        public async Task<IActionResult> NotificationCallback(
            [FromQuery(Name = "hub.topic")] string topic,
            [FromQuery(Name = "hub.challenge")] string challenge,
            [FromQuery(Name = "hub.mode")] string mode,
            [FromQuery(Name = "hub.lease_seconds")] string leaseSeconds,
            [FromQuery(Name = "hub.reason")] string reason,
            [FromQuery] string channelId,
            [FromQuery] string token,
            CancellationToken cancellationToken)
        {
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            try
            {
                if (HttpMethods.IsPost(Request.Method))
                    return await HandleNotificationAsync(channelId, token, cancellationToken);

                if (!HttpMethods.IsGet(Request.Method))
                {
                    _logger.LogWarning("NotificationCallback 收到不支援的 HTTP method：{Method}", Request.Method);
                    return new ContentResult { StatusCode = StatusCodes.Status400BadRequest };
                }

                if (string.IsNullOrEmpty(mode))
                    return new ContentResult { StatusCode = StatusCodes.Status400BadRequest };

                // PubSubHubbub 0.4 的 denied 通知沒有 hub.challenge，不可走 challenge 分支。
                if (mode == YoutubeWebSubContract.ModeDenied)
                    return await HandleDeniedAsync(topic, channelId, token, reason, cancellationToken);

                return await HandleChallengeAsync(mode, topic, challenge, leaseSeconds, channelId, token, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Redis／MySQL 更新失敗時保留未確認 pending，讓 Hub 重試；不可只完成一半後回 2xx。
                _logger.LogError(ex, "NotificationCallback 處理失敗。");
                return new ContentResult { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

        private async Task<IActionResult> HandleChallengeAsync(
            string mode, string topic, string challenge, string leaseSeconds,
            string channelId, string token, CancellationToken cancellationToken)
        {
            string topicChannelId = YoutubeWebSubContract.ExtractChannelIdFromTopic(topic);
            YoutubeWebSubPendingSnapshot pending = topicChannelId == null
                ? null
                : await _state.GetPendingSnapshotAsync(topicChannelId);
            string secret = topicChannelId == null
                ? null
                : await _state.GetSecretAsync(topicChannelId);

            YoutubeWebSubChallengeEvaluation evaluation = await YoutubeWebSubCallbackPolicy.EvaluateChallengeAsync(
                mode, topic, challenge, leaseSeconds, channelId, token, pending?.Action, secret,
                () => ChannelExistsAsync(topicChannelId, cancellationToken));

            switch (evaluation.Action)
            {
                case YoutubeWebSubChallengeAction.BadRequest:
                    LogEvaluation(evaluation, topicChannelId, mode);
                    return new ContentResult { StatusCode = StatusCodes.Status400BadRequest };

                case YoutubeWebSubChallengeAction.NotFound:
                    LogEvaluation(evaluation, topicChannelId, mode);
                    return NotFound();

                case YoutubeWebSubChallengeAction.ServerError:
                    LogEvaluation(evaluation, topicChannelId, mode);
                    return new ContentResult { StatusCode = StatusCodes.Status500InternalServerError };

                case YoutubeWebSubChallengeAction.AlreadyConfirmed:
                    // Hub 沒收到我們的 2xx 而重試同一 challenge：不更新任何狀態，逐字回傳 challenge。
                    // LastSubscribeTime 早在第一次確認時就寫入（順序是 secret TTL → DB → confirmed），
                    // 重試改寫只會讓訂閱時間往前漂移、延後續訂。
                    return ChallengeResponse(challenge);
            }

            if (evaluation.Action == YoutubeWebSubChallengeAction.ConfirmSubscribe)
            {
                // 計畫 §6.1／§8.4 的順序：secret TTL → DB → confirmed。
                // 每一步都以 pending 原始值做 compare-and-set，pending 已被換掉時不留下半套狀態。
                if (!await _state.TrySetSecretTtlAsync(pending, evaluation.LeaseTtl.Value))
                {
                    _logger.LogWarning("WebSub subscribe challenge 無法更新 secret TTL（pending 已變更或 secret 消失）| Channel: {ChannelId}", topicChannelId);
                    return new ContentResult { StatusCode = StatusCodes.Status500InternalServerError };
                }

                if (!await TryUpdateLastSubscribeTimeAsync(topicChannelId, cancellationToken))
                    return NotFound();
            }

            bool confirmed = await _state.TryMarkConfirmedAsync(
                pending, deleteSecret: evaluation.Action == YoutubeWebSubChallengeAction.ConfirmUnsubscribe);
            if (!confirmed)
            {
                // pending 在驗證後被 Bot 換掉：不覆寫新狀態，回 5xx 讓 Hub 重試（重試會對到新的 pending）。
                _logger.LogWarning("WebSub {Mode} challenge 期間 pending 已變更，未標記確認 | Channel: {ChannelId}", mode, topicChannelId);
                return new ContentResult { StatusCode = StatusCodes.Status500InternalServerError };
            }

            _logger.LogInformation("WebSub {Mode} challenge 成功 | Channel: {ChannelId}", mode, topicChannelId);
            return ChallengeResponse(challenge);
        }

        /// <summary>
        /// LastSubscribeTime 只在 challenge 成功後更新；DB 例外必須回 5xx，不可與 pending／Redis 狀態不一致。
        /// 回傳 false 代表 crawler 已不存在。
        /// </summary>
        private async Task<bool> TryUpdateLastSubscribeTimeAsync(string channelId, CancellationToken cancellationToken)
        {
            var spider = await _db.YoutubeChannelSpider.FirstOrDefaultAsync(x => x.ChannelId == channelId, cancellationToken);
            if (spider == null)
            {
                _logger.LogWarning("WebSub subscribe challenge 時資料庫已無此爬蟲，拒絕 | Channel: {ChannelId}", channelId);
                return false;
            }

            spider.LastSubscribeTime = DateTime.Now;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        private async Task<IActionResult> HandleDeniedAsync(
            string topic, string channelId, string token, string reason, CancellationToken cancellationToken)
        {
            string topicChannelId = YoutubeWebSubContract.ExtractChannelIdFromTopic(topic);
            YoutubeWebSubPendingSnapshot pending = topicChannelId == null
                ? null
                : await _state.GetPendingSnapshotAsync(topicChannelId);

            YoutubeWebSubChallengeEvaluation evaluation =
                YoutubeWebSubCallbackPolicy.EvaluateDenied(topic, channelId, token, pending?.Action);

            if (evaluation.Action == YoutubeWebSubChallengeAction.NotFound)
            {
                LogEvaluation(evaluation, topicChannelId, YoutubeWebSubContract.ModeDenied);
                return NotFound();
            }

            if (evaluation.Action == YoutubeWebSubChallengeAction.BadRequest)
            {
                LogEvaluation(evaluation, topicChannelId, YoutubeWebSubContract.ModeDenied);
                return new ContentResult { StatusCode = StatusCodes.Status400BadRequest };
            }

            if (evaluation.Action == YoutubeWebSubChallengeAction.ConfirmDenied)
            {
                // 不更新 LastSubscribeTime、HMAC secret TTL 或 confirmed 狀態。
                bool recorded = await _state.TryMarkDeniedAsync(pending);
                if (!recorded)
                {
                    // CAS 失敗代表 pending 在驗證後被更新：重讀一次，仍是同一筆要求就再標記一次。
                    pending = await _state.GetPendingSnapshotAsync(topicChannelId);
                    if (pending?.Action != null
                        && pending.Action.Topic == topic
                        && pending.Action.DeniedAtUtc == null
                        && YoutubeWebSubContract.FixedTimeEquals(pending.Action.CallbackToken, token))
                    {
                        recorded = await _state.TryMarkDeniedAsync(pending);
                    }
                }

                if (!recorded)
                {
                    _logger.LogWarning("WebSub denied 期間 pending 已變更，未標記 denied | Channel: {ChannelId}", topicChannelId);
                    return new ContentResult { StatusCode = StatusCodes.Status200OK };
                }

                // hub.reason 是外部輸入：只記錄安全摘要（移除 token）且不進使用者訊息或 metric label。
                _logger.LogWarning("WebSub 訂閱被 Hub 拒絕 | Channel: {ChannelId} | Mode: {Mode} | Reason: {Reason}",
                    topicChannelId, pending.Action.Mode, YoutubeWebSubContract.Summarize(reason, sensitiveValues: [token]));
            }

            return new ContentResult { StatusCode = StatusCodes.Status200OK };
        }

        private async Task<IActionResult> HandleNotificationAsync(string queryChannelId, string token, CancellationToken cancellationToken)
        {
            byte[] body = await ReadBodyAsync(cancellationToken);

            YoutubeWebSubNotificationEvaluation evaluation = await YoutubeWebSubCallbackPolicy.EvaluateNotificationAsync(
                body,
                Request.ContentType,
                Request.Headers["X-Hub-Signature"],
                queryChannelId,
                token,
                channelId => GetSecretAsync(channelId, cancellationToken),
                channelId => ChannelIsCrawled(channelId));

            switch (evaluation.Action)
            {
                case YoutubeWebSubNotificationAction.NeedRegister:
                    _logger.LogWarning("{Diagnostic}，要求重新訂閱 | Channel: {ChannelId}",
                        evaluation.Diagnostic, evaluation.ChannelId);
                    await _redisService.AddPubMessageAsync("youtube.pubsub.NeedRegister", evaluation.ChannelId);
                    break;

                case YoutubeWebSubNotificationAction.Publish:
                    _logger.LogInformation("{Data}", evaluation.Notification.ToString());
                    await _redisService.AddYouTubePubMessageAsync(evaluation.Notification);
                    break;

                default:
                    _logger.LogWarning("{Diagnostic} | Channel: {ChannelId}", evaluation.Diagnostic, evaluation.ChannelId);
                    break;
            }

            return new ContentResult { StatusCode = StatusCodes.Status200OK };
        }

        private ContentResult ChallengeResponse(string challenge)
            => new()
            {
                StatusCode = StatusCodes.Status200OK,
                Content = challenge,
                ContentType = "text/plain; charset=utf-8"
            };

        private async Task<byte[]> ReadBodyAsync(CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }

        private async Task<string> GetSecretAsync(string channelId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _state.GetSecretAsync(channelId);
        }

        /// <summary>
        /// challenge 路徑的 crawler 檢查：MySQL 例外必須往外傳，由呼叫端回 5xx 讓 Hub 重試，
        /// 不可把暫時性 DB 故障誤判成「crawler 不存在」而回 404。
        /// </summary>
        private Task<bool> ChannelExistsAsync(string channelId, CancellationToken cancellationToken)
            => _db.YoutubeChannelSpider.AsNoTracking().AnyAsync(x => x.ChannelId == channelId, cancellationToken);

        /// <summary>通知 POST 路徑的 crawler 檢查：查詢失敗時不發布 NeedRegister，但仍回 2xx。</summary>
        private bool ChannelIsCrawled(string channelId)
        {
            try
            {
                return _db.YoutubeChannelSpider.AsNoTracking().Any(x => x.ChannelId == channelId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢 crawler 失敗 | Channel: {ChannelId}", channelId);
                return false;
            }
        }

        private void LogEvaluation(YoutubeWebSubChallengeEvaluation evaluation, string topicChannelId, string mode)
        {
            if (evaluation.Action == YoutubeWebSubChallengeAction.ServerError)
                _logger.LogError("{Diagnostic} | Channel: {ChannelId} | Mode: {Mode}", evaluation.Diagnostic, topicChannelId, mode);
            else
                _logger.LogWarning("{Diagnostic} | Channel: {ChannelId} | Mode: {Mode}", evaluation.Diagnostic, topicChannelId, mode);
        }
    }
}
