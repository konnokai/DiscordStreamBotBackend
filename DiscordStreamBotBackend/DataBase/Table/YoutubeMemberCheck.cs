using System;
using System.ComponentModel.DataAnnotations;

namespace DiscordStreamBotBackend.DataBase.Table
{
    public class YoutubeMemberCheck
    {
        [Key]
        public int Id { get; set; }
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public string CheckYtChannelId { get; set; }
        public DateTime LastCheckTime { get; set; } = DateTime.Now;
        public bool IsChecked { get; set; } = false;
        public bool PendingRoleRemoval { get; set; }
        public DateTime? DateAdded { get; set; }
    }
}
