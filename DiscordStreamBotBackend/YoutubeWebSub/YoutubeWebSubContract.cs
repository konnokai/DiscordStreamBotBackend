using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Newtonsoft.Json;

namespace DiscordStreamBotBackend.YoutubeWebSub;

/// <summary>
/// YouTube WebSub（PubSubHubbub 0.4）契約的 Backend 端實作：canonical topic、pending action 格式與
/// callback token 衍生。與 Bot 的 <c>DiscordStreamNotifyBot.SharedService.Youtube.YoutubeWebSubContract</c>
/// 必須完全一致，兩邊各有一份契約測試鎖住衍生結果與 JSON 欄位。
/// </summary>
public static class YoutubeWebSubContract
{
    /// <summary>canonical topic 前綴；官方文件為 <c>https://www.youtube.com/feeds/videos.xml?channel_id=</c>。</summary>
    public const string TopicPrefix = "https://www.youtube.com/feeds/videos.xml?channel_id=";

    public const string ModeSubscribe = "subscribe";
    public const string ModeUnsubscribe = "unsubscribe";
    public const string ModeDenied = "denied";

    public const string NotificationContentType = "application/atom+xml";

    /// <summary>callback token 衍生的固定用途字串；不隨頻道改變，只憑 HMAC secret 即可重算。</summary>
    internal const string CallbackTokenPurpose = "discord-stream-bot:youtube-websub-callback:v1";

    public static bool IsValidChannelId(string channelId)
        => !string.IsNullOrEmpty(channelId)
           && channelId.Length == 24
           && channelId.StartsWith("UC", StringComparison.Ordinal)
           && channelId.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>由 <c>hub.topic</c> 取出 channel ID；不是 canonical topic 時回傳 null。</summary>
    public static string ExtractChannelIdFromTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic) || !topic.StartsWith(TopicPrefix, StringComparison.Ordinal))
            return null;

        string value = topic[TopicPrefix.Length..];
        int end = value.IndexOfAny(['&', '#']);
        if (end >= 0)
            value = value[..end];

        return IsValidChannelId(value) ? value : null;
    }

    /// <summary>由 HMAC secret 以 HMAC-SHA256 與固定用途字串衍生 callback token（與 Bot 相同演算法）。</summary>
    public static string DeriveCallbackToken(string hmacSecret)
    {
        if (string.IsNullOrEmpty(hmacSecret))
            throw new ArgumentException("HMAC secret 不得為空", nameof(hmacSecret));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(hmacSecret));
        return ToBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(CallbackTokenPurpose)));
    }

    /// <summary>以 media type 判斷通知的 Content-Type；允許 <c>charset</c> 等合法參數。</summary>
    public static bool IsAtomContentType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        try
        {
            return string.Equals(
                System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType).MediaType,
                NotificationContentType,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>constant-time 比較 token 或 signature，避免一般字串相等造成的時間側通道。</summary>
    public static bool FixedTimeEquals(string left, string right)
    {
        if (left == null || right == null)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
    }

    /// <summary>
    /// 將外部輸入（例如 <c>hub.reason</c>）收斂成有長度上限、去除控制字元、並移除指定敏感值的診斷摘要。
    /// Hub 或代理的錯誤內容可能反射回我們送出的 callback URL，因此 token 必須先移除再截短。
    /// </summary>
    public static string Summarize(string value, int maxLength = 200, params string[] sensitiveValues)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        foreach (string sensitive in sensitiveValues)
        {
            if (string.IsNullOrEmpty(sensitive))
                continue;

            value = value.Replace(sensitive, "[redacted]", StringComparison.Ordinal);
            string escaped = Uri.EscapeDataString(sensitive);
            if (escaped != sensitive)
                value = value.Replace(escaped, "[redacted]", StringComparison.Ordinal);
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (char c in value)
        {
            if (builder.Length >= maxLength)
                break;
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    internal static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Redis DB 1 的 WebSub pending action（計畫 §7.1）；欄位名稱與 Bot 完全相同。</summary>
public sealed class YoutubeWebSubPendingAction
{
    public const int CurrentVersion = 1;

    [JsonProperty("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonProperty("channelId")]
    public string ChannelId { get; set; }

    [JsonProperty("mode")]
    public string Mode { get; set; }

    [JsonProperty("topic")]
    public string Topic { get; set; }

    [JsonProperty("callbackToken")]
    public string CallbackToken { get; set; }

    [JsonProperty("requestedAtUtc")]
    public DateTime RequestedAtUtc { get; set; }

    [JsonProperty("confirmedAtUtc")]
    public DateTime? ConfirmedAtUtc { get; set; }

    [JsonProperty("deniedAtUtc")]
    public DateTime? DeniedAtUtc { get; set; }

    public bool Matches(string mode, string topic)
        => Mode == mode && Topic == topic;

    /// <summary>嚴格解析 Redis 內既有資料；格式不符時視為沒有 pending action，不接受未發出的訂閱要求。</summary>
    public static bool TryParse(string json, out YoutubeWebSubPendingAction action, out string error)
    {
        action = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "payload 為空";
            return false;
        }

        YoutubeWebSubPendingAction parsed;
        try
        {
            parsed = JsonConvert.DeserializeObject<YoutubeWebSubPendingAction>(json);
        }
            catch (JsonException)
            {
                // 不帶入例外訊息：它可能夾帶含 callbackToken 的 payload 片段。
                error = "JSON 解析失敗";
                return false;
            }

        if (parsed == null)
        {
            error = "JSON 解析結果為 null";
            return false;
        }

        if (parsed.Version != CurrentVersion)
            error = $"version 不支援：{parsed.Version}";
        else if (!YoutubeWebSubContract.IsValidChannelId(parsed.ChannelId))
            error = "channelId 格式不正確";
        else if (parsed.Mode is not (YoutubeWebSubContract.ModeSubscribe or YoutubeWebSubContract.ModeUnsubscribe))
            error = $"mode 不支援：{parsed.Mode}";
        else if (parsed.Topic != YoutubeWebSubContract.TopicPrefix + parsed.ChannelId)
            error = "topic 與 channelId 不符";
        else if (string.IsNullOrWhiteSpace(parsed.CallbackToken))
            error = "callbackToken 為空";
        else if (parsed.RequestedAtUtc == default)
            error = "requestedAtUtc 為空";

        if (error != null)
            return false;

        action = parsed;
        return true;
    }

    /// <summary>測試與診斷用的固定字串（不含 secret）。</summary>
    public override string ToString()
        => $"v{Version} {Mode} {ChannelId} requested={RequestedAtUtc:O} confirmed={ConfirmedAtUtc:O} denied={DeniedAtUtc:O}";
}
