using DiscordStreamBotBackend.DataBase.Table;
using Microsoft.EntityFrameworkCore;

namespace DiscordStreamBotBackend.DataBase;

public partial class MainDbContext(DbContextOptions<MainDbContext> options) : DbContext(options)
{
    public virtual DbSet<TwitchBroadcasterAuthorization> TwitchBroadcasterAuthorization { get; set; }
    public virtual DbSet<GoogleOAuthUnlinkIntent> GoogleOAuthUnlinkIntent { get; set; }
    public virtual DbSet<YoutubeChannelSpider> YoutubeChannelSpider { get; set; }
    public virtual DbSet<YoutubeMemberAccessToken> YoutubeMemberAccessToken { get; set; }
    public virtual DbSet<YoutubeMemberCheck> YoutubeMemberCheck { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TwitchBroadcasterAuthorization>(entity =>
        {
            entity.HasKey(x => x.TwitchUserId);
            entity.HasIndex(x => x.DiscordUserId).IsUnique();
            entity.Property(x => x.TwitchUserId).IsRequired();
            entity.Property(x => x.ClientId).IsRequired();
            entity.Property(x => x.UserLogin).IsRequired();
            entity.Property(x => x.DisplayName).IsRequired();
            entity.Property(x => x.ProfileImageUrl).IsRequired();
            entity.Property(x => x.Scopes).IsRequired();
            entity.Property(x => x.TokenExpiresAt).HasColumnType("datetime(6)");
            entity.Property(x => x.LastValidatedAt).HasColumnType("datetime(6)");
            entity.Property(x => x.AuthorizedAt).HasColumnType("datetime(6)");
            entity.Property(x => x.RevokedAt).HasColumnType("datetime(6)");
            entity.Property(x => x.DateUpdated)
                .HasColumnType("datetime(6)")
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<GoogleOAuthUnlinkIntent>(entity =>
        {
            entity.ToTable("google_oauth_unlink_intent");
            entity.Property(x => x.ExpectedEncryptedToken).HasColumnType("longtext").IsRequired(false);
            entity.Property(x => x.DateAdded).HasColumnType("datetime(6)");
        });

        modelBuilder.Entity<YoutubeMemberCheck>(entity =>
        {
            entity.Property(x => x.CheckYtChannelId).HasColumnType("longtext").IsRequired();
            entity.HasIndex(x => new { x.GuildId, x.UserId, x.CheckYtChannelId })
                .IsUnique()
                .HasPrefixLength(0, 0, 24);
            entity.HasIndex(x => new { x.PendingRoleRemoval, x.GuildId });
            entity.HasIndex(x => new { x.UserId, x.PendingRoleRemoval });
        });
    }
}
