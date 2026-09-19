using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace DiscordStreamBotBackend.YoutubeWebSub;

/// <summary>challenge／denied GET 的處置動作（計畫 §8.4、§8.5）。</summary>
internal enum YoutubeWebSubChallengeAction
{
    BadRequest,
    NotFound,
    ServerError,
    ConfirmSubscribe,
    ConfirmUnsubscribe,
    ConfirmDenied,
    AlreadyConfirmed,
}

internal readonly record struct YoutubeWebSubChallengeEvaluation(
    YoutubeWebSubChallengeAction Action,
    string Diagnostic,
    TimeSpan? LeaseTtl = null);

/// <summary>通知 POST 的處置動作（計畫 §8.6）。</summary>
internal enum YoutubeWebSubNotificationAction
{
    /// <summary>無效或無法驗證；忽略內容但回 2xx，避免 Hub 重送。</summary>
    Ignore,

    /// <summary>secret 缺失：忽略內容並要求 Bot 補送訂閱。</summary>
    NeedRegister,

    /// <summary>驗證通過，發布到既有 Redis channel。</summary>
    Publish,
}

internal readonly record struct YoutubeWebSubNotificationEvaluation(
    YoutubeWebSubNotificationAction Action,
    YoutubePubSubNotification Notification,
    string ChannelId,
    string Diagnostic);

/// <summary>
/// WebSub callback 的決策邏輯（計畫 §8.4～§8.6）：純粹驗證 mode／topic／channel／token／secret／HMAC
/// 與 Atom 內容，外部狀態只透過委派查詢，讓 controller 只負責 HTTP 與 Redis／MySQL 存取。
/// </summary>
internal static class YoutubeWebSubCallbackPolicy
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Youtube = "http://www.youtube.com/xml/schemas/2015";

    /// <summary>刪除通知使用 atompub tombstone（<c>at:deleted-entry</c>），與一般 feed 的 namespace 不同。</summary>
    private static readonly XNamespace Tombstone = "http://purl.org/atompub/tombstones/1.0";

    /// <summary>
    /// 驗證 Hub 的訂閱意圖 GET。只有與實際送出 pending action 完全相符的要求才會回傳可以確認的動作。
    /// </summary>
    /// <param name="leaseSeconds">subscribe 時必須存在的 Hub 實際 lease。</param>
    /// <param name="secret">該頻道目前的 HMAC secret；unsubscribe 時可為 null。</param>
    /// <param name="channelExistsAsync">subscribe 時確認 DB 仍存在該 crawler。</param>
    internal static async Task<YoutubeWebSubChallengeEvaluation> EvaluateChallengeAsync(
        string mode, string topic, string challenge, string leaseSeconds, string channelId, string token,
        YoutubeWebSubPendingAction pending, string secret, Func<Task<bool>> channelExistsAsync)
    {
        if (mode is not (YoutubeWebSubContract.ModeSubscribe or YoutubeWebSubContract.ModeUnsubscribe))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.BadRequest, $"未知的 mode：{mode}");

        if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(topic))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.BadRequest, "缺少 hub.challenge 或 hub.topic");

        TimeSpan? leaseTtl = null;
        if (mode == YoutubeWebSubContract.ModeSubscribe)
        {
            if (!int.TryParse(leaseSeconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds <= 0)
                return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.BadRequest, "缺少有效的 hub.lease_seconds");

            leaseTtl = TimeSpan.FromSeconds(seconds);
        }

        string topicChannelId = YoutubeWebSubContract.ExtractChannelIdFromTopic(topic);
        if (topicChannelId == null
            || channelId != topicChannelId
            || string.IsNullOrEmpty(token))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "topic／channelId／token 無效");

        if (pending == null || !pending.Matches(mode, topic))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "找不到相符的 pending action");

        if (!YoutubeWebSubContract.FixedTimeEquals(pending.CallbackToken, token))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "callback token 與 pending 不符");

        if (mode == YoutubeWebSubContract.ModeSubscribe)
        {
            if (string.IsNullOrEmpty(secret))
                return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.ServerError, "Redis 找不到 HMAC secret");

            if (!YoutubeWebSubContract.FixedTimeEquals(YoutubeWebSubContract.DeriveCallbackToken(secret), token))
                return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "callback token 與 HMAC secret 衍生值不符");

            if (!await channelExistsAsync())
                return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "資料庫已無此 crawler");
        }

        // 已確認 pending 的重複 challenge 必須 idempotent：不再更新狀態或旋轉 secret。
        if (pending.ConfirmedAtUtc != null)
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.AlreadyConfirmed, null);

        return new YoutubeWebSubChallengeEvaluation(
            mode == YoutubeWebSubContract.ModeSubscribe
                ? YoutubeWebSubChallengeAction.ConfirmSubscribe
                : YoutubeWebSubChallengeAction.ConfirmUnsubscribe,
            null,
            leaseTtl);
    }

    /// <summary>Hub 依 0.4 送出的 denied：沒有 challenge，只驗證 pending 與 token。</summary>
    internal static YoutubeWebSubChallengeEvaluation EvaluateDenied(
        string topic, string channelId, string token, YoutubeWebSubPendingAction pending)
    {
        if (string.IsNullOrEmpty(topic))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.BadRequest, "缺少 hub.topic");

        string topicChannelId = YoutubeWebSubContract.ExtractChannelIdFromTopic(topic);
        if (topicChannelId == null
            || channelId != topicChannelId
            || string.IsNullOrEmpty(token))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "topic／channelId／token 無效");

        if (pending == null || pending.Topic != topic)
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "找不到相符的 pending action");

        if (!YoutubeWebSubContract.FixedTimeEquals(pending.CallbackToken, token))
            return new YoutubeWebSubChallengeEvaluation(YoutubeWebSubChallengeAction.NotFound, "callback token 與 pending 不符");

        return new YoutubeWebSubChallengeEvaluation(
            pending.DeniedAtUtc == null ? YoutubeWebSubChallengeAction.ConfirmDenied : YoutubeWebSubChallengeAction.AlreadyConfirmed,
            null);
    }

    /// <summary>
    /// 驗證 Hub 的通知 POST：Content-Type、X-Hub-Signature、channelId／token 成對出現、raw-body HMAC 與 Atom 內容。
    /// 任一步失敗都回 <see cref="YoutubeWebSubNotificationAction.Ignore"/>，因為 Hub 重送同樣內容不會成功。
    /// <para>
    /// 帶 query 的新格式先用 channelId 讀 secret 並驗 token／HMAC，通過後才解析 XML；
    /// 只有完全沒有 query 資訊的部署前舊 callback 才需要先解析內容找 channel。
    /// </para>
    /// </summary>
    internal static async Task<YoutubeWebSubNotificationEvaluation> EvaluateNotificationAsync(
        byte[] body,
        string contentType,
        string signatureHeader,
        string queryChannelId,
        string token,
        Func<string, Task<string>> secretLookup,
        Func<string, bool> channelIsCrawled)
    {
        if (!YoutubeWebSubContract.IsAtomContentType(contentType))
            return Ignore(null, null, "Content-Type 無效");

        if (!TryReadSignature(signatureHeader, out string algorithm, out byte[] signature))
            return Ignore(null, null, "缺少有效的 X-Hub-Signature");

        // channelId 與 token 必須成對出現：只有 deployment 前的舊 callback 兩者皆無。
        bool hasChannel = !string.IsNullOrEmpty(queryChannelId);
        bool hasToken = !string.IsNullOrEmpty(token);
        if (hasChannel != hasToken)
            return Ignore(null, null, "channelId 與 token 必須同時提供");

        if (hasChannel)
        {
            string querySecret = await secretLookup(queryChannelId);
            if (string.IsNullOrEmpty(querySecret))
            {
                return channelIsCrawled(queryChannelId)
                    ? new YoutubeWebSubNotificationEvaluation(YoutubeWebSubNotificationAction.NeedRegister, null, queryChannelId, "Redis 找不到 HMAC secret")
                    : Ignore(null, queryChannelId, "Redis 找不到 HMAC secret 且非本系統 crawler");
            }

            if (!YoutubeWebSubContract.FixedTimeEquals(YoutubeWebSubContract.DeriveCallbackToken(querySecret), token))
                return Ignore(null, queryChannelId, "callback token 無效");

            if (!VerifySignature(body, querySecret, algorithm, signature))
                return Ignore(null, queryChannelId, "HMAC 驗證失敗");

            YoutubePubSubNotification parsed = ParseAtom(body);
            if (parsed == null)
                return Ignore(null, queryChannelId, "Atom 內容無法解析");

            if (parsed.ChannelId != queryChannelId)
                return Ignore(parsed, queryChannelId, "payload channel 與 query 不符");

            return Publish(parsed);
        }

        YoutubePubSubNotification notification = ParseAtom(body);
        if (notification == null)
            return Ignore(null, null, "Atom 內容無法解析");

        string secret = await secretLookup(notification.ChannelId);
        if (string.IsNullOrEmpty(secret))
        {
            // secret 缺失才可要求重新訂閱；僅限確實是本系統的 crawler，避免外部請求放大。
            return channelIsCrawled(notification.ChannelId)
                ? new YoutubeWebSubNotificationEvaluation(YoutubeWebSubNotificationAction.NeedRegister, notification, notification.ChannelId, "Redis 找不到 HMAC secret")
                : Ignore(notification, notification.ChannelId, "Redis 找不到 HMAC secret 且非本系統 crawler");
        }

        if (!VerifySignature(body, secret, algorithm, signature))
            return Ignore(notification, notification.ChannelId, "HMAC 驗證失敗");

        return Publish(notification);
    }

    private static YoutubeWebSubNotificationEvaluation Publish(YoutubePubSubNotification notification)
        => new(YoutubeWebSubNotificationAction.Publish, notification, notification.ChannelId, null);

    private static YoutubeWebSubNotificationEvaluation Ignore(
        YoutubePubSubNotification notification, string channelId, string diagnostic)
        => new(YoutubeWebSubNotificationAction.Ignore, notification, channelId, diagnostic);

    /// <summary>解析 <c>algorithm=hex</c> 形式的 X-Hub-Signature；至少支援 Google Hub 使用的 sha1。</summary>
    internal static bool TryReadSignature(string headerValue, out string algorithm, out byte[] signature)
    {
        algorithm = null;
        signature = null;

        if (string.IsNullOrEmpty(headerValue))
            return false;

        int separator = headerValue.IndexOf('=');
        if (separator <= 0 || separator == headerValue.Length - 1)
            return false;

        algorithm = headerValue[..separator].Trim().ToLowerInvariant();
        int expectedLength = algorithm switch
        {
            "sha1" => 40,
            "sha256" => 64,
            _ => -1
        };
        if (expectedLength < 0)
            return false;

        string hex = headerValue[(separator + 1)..].Trim();
        if (hex.Length != expectedLength || !hex.All(Uri.IsHexDigit))
            return false;

        signature = Convert.FromHexString(hex);
        return true;
    }

    /// <summary>對原始 request body bytes 計算 HMAC，並以 constant-time 比較。</summary>
    internal static bool VerifySignature(byte[] body, string secret, string algorithm, byte[] expected)
    {
        if (body == null || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(algorithm) || expected == null)
            return false;

        byte[] key = Encoding.UTF8.GetBytes(secret);
        using HMAC hmac = algorithm == "sha256" ? new HMACSHA256(key) : new HMACSHA1(key);
        byte[] computed = hmac.ComputeHash(body);

        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }

    /// <summary>安全解析 Atom 通知；禁止 DTD／外部 entity，且只在既有 namespace 中取值。</summary>
    internal static YoutubePubSubNotification ParseAtom(byte[] body)
    {
        if (body == null || body.Length == 0)
            return null;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = false,
            };

            using var memory = new MemoryStream(body, writable: false);
            using var reader = XmlReader.Create(memory, settings);
            XDocument document = XDocument.Load(reader, LoadOptions.None);

            XElement deletedEntry = document.Descendants(Tombstone + "deleted-entry").FirstOrDefault();
            if (deletedEntry != null)
                return ReadDeletedEntry(deletedEntry);

            XElement entry = document.Descendants(Atom + "entry").FirstOrDefault();
            return entry == null ? null : ReadCreateOrUpdateEntry(entry);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static YoutubePubSubNotification ReadCreateOrUpdateEntry(XElement entry)
    {
        string videoId = (string)entry.Element(Youtube + "videoId");
        string channelId = (string)entry.Element(Youtube + "channelId");
        if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(channelId))
            return null;

        string link = entry.Elements(Atom + "link")
            .Where(static x => string.Equals((string)x.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
            .Select(static x => (string)x.Attribute("href"))
            .FirstOrDefault(static x => !string.IsNullOrEmpty(x));

        return new YoutubePubSubNotification
        {
            VideoId = videoId,
            ChannelId = channelId,
            Title = (string)entry.Element(Atom + "title"),
            Link = link,
            Published = ConvertDateTime((string)entry.Element(Atom + "published")),
            Updated = ConvertDateTime((string)entry.Element(Atom + "updated")),
            NotificationType = YoutubePubSubNotification.YTNotificationType.CreateOrUpdated,
        };
    }

    private static YoutubePubSubNotification ReadDeletedEntry(XElement deletedEntry)
    {
        string videoId = ExtractVideoIdFromReference((string)deletedEntry.Attribute("ref"));

        // 標準 tombstone 以 <at:by><uri> 表示頻道；uri 屬 Atom namespace，不接受任意 namespace 的同名節點。
        string uri = deletedEntry.Descendants()
            .Where(static x => x.Name.LocalName == "uri" && (x.Name.Namespace == Atom || x.Name.Namespace == Tombstone))
            .Select(static x => x.Value)
            .FirstOrDefault(static x => !string.IsNullOrWhiteSpace(x));

        string channelId = ExtractChannelIdFromUri(uri);
        if (string.IsNullOrEmpty(videoId) || string.IsNullOrEmpty(channelId))
            return null;

        return new YoutubePubSubNotification
        {
            VideoId = videoId,
            ChannelId = channelId,
            Published = ConvertDateTime((string)deletedEntry.Attribute("when")),
            NotificationType = YoutubePubSubNotification.YTNotificationType.Deleted,
        };
    }

    /// <summary><c>ref="yt:video:VIDEOID"</c> → VIDEOID。</summary>
    private static string ExtractVideoIdFromReference(string reference)
    {
        if (string.IsNullOrEmpty(reference))
            return null;

        int index = reference.LastIndexOf(':');
        return index < 0 ? reference : reference[(index + 1)..];
    }

    /// <summary><c>https://www.youtube.com/channel/UC...</c> → UC...。</summary>
    private static string ExtractChannelIdFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        const string marker = "/channel/";
        int index = uri.IndexOf(marker, StringComparison.Ordinal);
        string value = index < 0 ? uri : uri[(index + marker.Length)..];

        value = value.TrimEnd('/');
        int end = value.IndexOfAny(['?', '#']);
        if (end >= 0)
            value = value[..end];

        return YoutubeWebSubContract.IsValidChannelId(value) ? value : null;
    }

    private static DateTime ConvertDateTime(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return default;

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.UtcDateTime
            : default;
    }
}
