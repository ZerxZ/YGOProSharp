using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using YGOProSharp.Persistence.Entities;

namespace YGOProSharp.Persistence;

/// <summary>
/// 服务端运营关系型数据库上下文（支持 SQLite WAL 与 PostgreSQL 双 Provider，全局软删除过滤，基于 EF Core Migrations 演进表结构）。
/// </summary>
public sealed class OperationalDbContext : DbContext
{
    public OperationalDbContext(DbContextOptions<OperationalDbContext> options)
        : base(options)
    {
    }

    public DbSet<PanelUserEntity> PanelUsers => Set<PanelUserEntity>();
    public DbSet<PanelRoleEntity> PanelRoles => Set<PanelRoleEntity>();
    public DbSet<PanelUserRoleEntity> PanelUserRoles => Set<PanelUserRoleEntity>();
    public DbSet<PanelSessionEntity> PanelSessions => Set<PanelSessionEntity>();
    public DbSet<McpTokenEntity> McpTokens => Set<McpTokenEntity>();
    public DbSet<PlayerProfileEntity> PlayerProfiles => Set<PlayerProfileEntity>();
    public DbSet<DuelRecordEntity> DuelRecords => Set<DuelRecordEntity>();
    public DbSet<DuelRecordPlayerEntity> DuelRecordPlayers => Set<DuelRecordPlayerEntity>();
    public DbSet<BanEntity> Bans => Set<BanEntity>();
    public DbSet<TournamentDeckEntity> TournamentDecks => Set<TournamentDeckEntity>();
    public DbSet<DiyProposalEntity> DiyProposals => Set<DiyProposalEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<DeckEvaluationEntity> DeckEvaluations => Set<DeckEvaluationEntity>();
    public DbSet<DuelReviewEntity> DuelReviews => Set<DuelReviewEntity>();
    public DbSet<AiDuelistMemoryEntity> AiDuelistMemories => Set<AiDuelistMemoryEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureOperationalModel(modelBuilder);
    }

    internal static void ConfigureOperationalModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PanelUserEntity>(b =>
        {
            b.ToTable("panel_users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).HasMaxLength(64).IsRequired();
            b.Property(x => x.NormalizedUsername).HasMaxLength(64).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(x => x.LastLoginIp).HasMaxLength(64);
            b.HasIndex(x => x.NormalizedUsername).IsUnique();
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<PanelRoleEntity>(b =>
        {
            b.ToTable("panel_roles");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(64).IsRequired();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Description).HasMaxLength(512);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<PanelUserRoleEntity>(b =>
        {
            b.ToTable("panel_user_roles");
            b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne(x => x.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasQueryFilter(x => x.User.DeletedAt == null && x.Role.DeletedAt == null);
        });

        modelBuilder.Entity<PanelSessionEntity>(b =>
        {
            b.ToTable("panel_sessions");
            b.HasKey(x => x.Id);
            b.Property(x => x.TokenPrefix).HasMaxLength(32).IsRequired();
            b.Property(x => x.TokenSha256).HasMaxLength(64).IsRequired();
            b.Property(x => x.CsrfToken).HasMaxLength(64).IsRequired();
            b.Property(x => x.Ip).HasMaxLength(64);
            b.Property(x => x.UserAgent).HasMaxLength(512);
            b.HasIndex(x => x.TokenSha256).IsUnique();
            b.HasIndex(x => x.UserId);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<McpTokenEntity>(b =>
        {
            b.ToTable("mcp_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.OwnerUsername).HasMaxLength(64).IsRequired();
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.PreferredPlayerName).HasMaxLength(32);
            b.Property(x => x.PersonaStyle).HasMaxLength(128);
            b.Property(x => x.TokenPrefix).HasMaxLength(32).IsRequired();
            b.Property(x => x.TokenSha256).HasMaxLength(64).IsRequired();
            b.Property(x => x.McpRole).HasMaxLength(32).IsRequired();
            b.HasIndex(x => x.TokenSha256).IsUnique();
            b.HasIndex(x => x.OwnerUserId);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<PlayerProfileEntity>(b =>
        {
            b.ToTable("player_profiles");
            b.HasKey(x => x.Id);
            b.Property(x => x.PlayerName).HasMaxLength(64).IsRequired();
            b.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
            b.Property(x => x.VpassHash).HasMaxLength(512);
            b.Property(x => x.ClientKey).HasMaxLength(128);
            b.HasIndex(x => x.NormalizedName).IsUnique();
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<DuelRecordEntity>(b =>
        {
            b.ToTable("duel_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.RoomName).HasMaxLength(128).IsRequired();
            b.Property(x => x.RoomIdentifier).HasMaxLength(64).IsRequired();
            b.Property(x => x.RandomDeckMode).HasMaxLength(32).IsRequired();
            b.Property(x => x.YrpPath).HasMaxLength(512);
            b.Property(x => x.ObserverFramesPath).HasMaxLength(512);
            b.HasIndex(x => x.EndedAt);
            b.HasIndex(x => x.RoomIdentifier);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<DuelRecordPlayerEntity>(b =>
        {
            b.ToTable("duel_record_players");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.PlayerName).HasMaxLength(64).IsRequired();
            b.Property(x => x.RealName).HasMaxLength(64).IsRequired();
            b.Property(x => x.SeatType).HasMaxLength(32).IsRequired();
            b.Property(x => x.ClientIp).HasMaxLength(64);
            b.HasOne(x => x.DuelRecord)
                .WithMany(d => d.Players)
                .HasForeignKey(x => x.DuelRecordId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.DuelRecordId, x.SeatIndex });
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<BanEntity>(b =>
        {
            b.ToTable("bans");
            b.HasKey(x => x.Id);
            b.Property(x => x.TargetType).HasMaxLength(32).IsRequired();
            b.Property(x => x.TargetValue).HasMaxLength(128).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(512);
            b.Property(x => x.CreatedByName).HasMaxLength(64);
            b.HasIndex(x => new { x.TargetType, x.TargetValue });
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<TournamentDeckEntity>(b =>
        {
            b.ToTable("tournament_decks");
            b.HasKey(x => x.Id);
            b.Property(x => x.TournamentTag).HasMaxLength(64).IsRequired();
            b.Property(x => x.PlayerName).HasMaxLength(64).IsRequired();
            b.Property(x => x.NormalizedPlayerName).HasMaxLength(64).IsRequired();
            b.Property(x => x.DeckHash).HasMaxLength(64).IsRequired();
            b.HasIndex(x => new { x.TournamentTag, x.NormalizedPlayerName }).IsUnique();
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<DiyProposalEntity>(b =>
        {
            b.ToTable("diy_proposals");
            b.HasKey(x => x.Id);
            b.Property(x => x.PackageId).HasMaxLength(128).IsRequired();
            b.Property(x => x.Title).HasMaxLength(256).IsRequired();
            b.Property(x => x.Status).HasMaxLength(32).IsRequired();
            b.Property(x => x.ReviewComment).HasMaxLength(1024);
            b.HasIndex(x => x.Status);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<AuditLogEntity>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
            b.Property(x => x.ActorId).HasMaxLength(64);
            b.Property(x => x.ActorName).HasMaxLength(64).IsRequired();
            b.Property(x => x.Action).HasMaxLength(128).IsRequired();
            b.Property(x => x.PermissionChecked).HasMaxLength(64);
            b.Property(x => x.Target).HasMaxLength(256);
            b.Property(x => x.Ip).HasMaxLength(64);
            b.Property(x => x.Result).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.CreatedAt);
            b.HasIndex(x => x.Action);
            b.HasIndex(x => x.Result);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<DeckEvaluationEntity>(b =>
        {
            b.ToTable("deck_evaluations");
            b.HasKey(x => x.Id);
            b.Property(x => x.DeckHash).HasMaxLength(64).IsRequired();
            b.Property(x => x.DeckName).HasMaxLength(128).IsRequired();
            b.Property(x => x.SourceType).HasMaxLength(32).IsRequired();
            b.Property(x => x.SourceRef).HasMaxLength(128);
            b.Property(x => x.ArchetypeTitle).HasMaxLength(128).IsRequired();
            b.Property(x => x.OverallTier).HasMaxLength(8).IsRequired();
            b.Property(x => x.EvaluatorName).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.DeckHash);
            b.HasIndex(x => x.UpdatedAt);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<DuelReviewEntity>(b =>
        {
            b.ToTable("duel_reviews");
            b.HasKey(x => x.Id);
            b.Property(x => x.Headline).HasMaxLength(200).IsRequired();
            b.Property(x => x.ReviewerName).HasMaxLength(64).IsRequired();
            b.Property(x => x.ReviewerRole).HasMaxLength(32).IsRequired();
            b.HasIndex(x => x.DuelRecordId);
            b.HasIndex(x => x.CreatedAt);
            b.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<AiDuelistMemoryEntity>(b =>
        {
            b.ToTable("ai_duelist_memories");
            b.HasKey(x => x.Id);
            b.Property(x => x.DeckHash).HasMaxLength(64);
            b.Property(x => x.OpponentArchetype).HasMaxLength(128);
            b.Property(x => x.OpponentName).HasMaxLength(64);
            b.Property(x => x.Result).HasMaxLength(16).IsRequired();
            b.Property(x => x.StrategicNote).HasMaxLength(600).IsRequired();
            b.HasIndex(x => x.TokenId);
            b.HasIndex(x => new { x.TokenId, x.DeckHash });
            b.HasQueryFilter(x => x.DeletedAt == null);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTimestamps()
    {
        DateTime now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditedEntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null)
        {
        }
    }
}
