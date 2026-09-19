using System;

namespace DiscordStreamBotBackend.YoutubeWebSub;

/// <summary>
/// 由 Hub 轉發到 Redis 的 WebSub 通知 payload。
/// <para>
/// 這個形狀是 Bot 的 <c>youtube.pubsub.CreateOrUpdate</c>／<c>youtube.pubsub.Deleted</c>
/// 訂閱者既有契約（欄位名稱與 <c>NotificationType</c> 數值），不可任意變更。
/// </para>
/// </summary>
public class YoutubePubSubNotification
{
    public enum YTNotificationType { CreateOrUpdated, Deleted }

    public YTNotificationType NotificationType { get; set; } = YTNotificationType.CreateOrUpdated;
    public string VideoId { get; set; }
    public string ChannelId { get; set; }
    public string Title { get; set; }
    public string Link { get; set; }
    public DateTime Published { get; set; }
    public DateTime Updated { get; set; }

    public override string ToString()
    {
        switch (NotificationType)
        {
            case YTNotificationType.CreateOrUpdated:
                return $"({NotificationType} at {Updated}) {ChannelId} - {VideoId} | {Title}";
            case YTNotificationType.Deleted:
                return $"({NotificationType} at {Published}) {ChannelId} - {VideoId}";
            default:
                break;
        }
        return "";
    }
}
