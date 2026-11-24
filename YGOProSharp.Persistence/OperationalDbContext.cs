using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

/// <summary>
/// 服务端运营关系型数据库上下文（支持 SQLite WAL 与 PostgreSQL 双 Provider，全局软删除过滤）。
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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
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

public sealed record BootstrapAdminResult(
    bool CreatedNewAdmin,
    string Username,
    string? GeneratedOneTimePassword,
    bool MustChangePassword);

public sealed record DatabaseHealthSnapshot(
    string Provider,
    bool IsHealthy,
    double LatencyMs,
    string JournalMode,
    int ActiveUserCount,
    int ActiveBanCount,
    long TotalDuelRecords,
    string? ErrorMessage);

/// <summary>
/// 负责双引擎 DbContext 构造、SQLite WAL 初始化、角色与初始超管安全引导及健康检查。
/// </summary>
public sealed class OperationalDatabaseManager
{
    private readonly ILogger<OperationalDatabaseManager> _logger = AppLog.CreateLogger<OperationalDatabaseManager>();
    private readonly PersistenceOptions _options;
    private readonly DbContextOptions<OperationalDbContext> _dbContextOptions;
    private readonly string _normalizedConnectionString;

    public OperationalDatabaseManager(PersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _normalizedConnectionString = NormalizeConnectionString(options);
        _dbContextOptions = BuildDbContextOptions(options.DbProvider, _normalizedConnectionString);
    }

    public OperationalDbProvider Provider => _options.DbProvider;

    public OperationalDbContext CreateDbContext()
    {
        return new OperationalDbContext(_dbContextOptions);
    }

    public async Task<BootstrapAdminResult> InitializeAndSeedAsync(
        Argon2PasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passwordHasher);

        await using OperationalDbContext db = CreateDbContext();
        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        if (_options.DbProvider == OperationalDbProvider.Sqlite)
        {
            await db.Database.ExecuteSqlRawAsync(
                "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",
                cancellationToken).ConfigureAwait(false);
        }

        // 1. 种子化五级内置角色
        Dictionary<string, PanelRoleEntity> existingRoles = await db.PanelRoles
            .ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        PanelRoleEntity? superAdminRole = null;
        foreach (BuiltInRoleTemplate tpl in RbacPolicyEvaluator.BuiltInRoles)
        {
            if (!existingRoles.TryGetValue(tpl.Code, out PanelRoleEntity? role))
            {
                role = new PanelRoleEntity
                {
                    Code = tpl.Code,
                    Name = tpl.Name,
                    Description = tpl.Description,
                    IsBuiltIn = true,
                    PermissionsJson = RbacPolicyEvaluator.SerializePermissions(tpl.Permissions)
                };
                db.PanelRoles.Add(role);
                existingRoles[tpl.Code] = role;
            }
            else
            {
                role.IsBuiltIn = true;
            }

            if (string.Equals(tpl.Code, BuiltInRoleCodes.SuperAdmin, StringComparison.OrdinalIgnoreCase))
                superAdminRole = role;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 2. 检查是否已存在有效的超级管理员账号
        bool hasSuperAdmin = await db.PanelUserRoles
            .AnyAsync(ur => ur.Role.Code == BuiltInRoleCodes.SuperAdmin && ur.User.Enabled, cancellationToken)
            .ConfigureAwait(false);

        if (hasSuperAdmin)
        {
            PanelUserEntity firstAdmin = await db.PanelUsers
                .Where(u => u.Enabled && u.UserRoles.Any(ur => ur.Role.Code == BuiltInRoleCodes.SuperAdmin))
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);
            return new BootstrapAdminResult(false, firstAdmin.Username, null, firstAdmin.MustChangePassword);
        }

        string adminUsername = ResolveBootstrapSetting(_options.BootstrapAdminUser, "YGOPRO_BOOTSTRAP_ADMIN_USER") ?? "admin";
        string? configuredPassword = ResolveBootstrapSetting(_options.BootstrapAdminPassword, "YGOPRO_BOOTSTRAP_ADMIN_PASSWORD");
        string? generatedPassword = null;
        bool mustChangePassword;
        string passwordToHash;

        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            passwordToHash = configuredPassword;
            mustChangePassword = false;
        }
        else
        {
            generatedPassword = TokenCrypto.GenerateBootstrapPassword(24);
            passwordToHash = generatedPassword;
            mustChangePassword = true;
        }

        string normalizedUsername = adminUsername.Trim().ToLowerInvariant();
        PanelUserEntity? existingUser = await db.PanelUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken)
            .ConfigureAwait(false);

        if (existingUser is null)
        {
            existingUser = new PanelUserEntity
            {
                Username = adminUsername.Trim(),
                NormalizedUsername = normalizedUsername,
                DisplayName = "Administrator",
                PasswordHash = passwordHasher.HashPassword(passwordToHash),
                Enabled = true,
                MustChangePassword = mustChangePassword
            };
            db.PanelUsers.Add(existingUser);
        }
        else
        {
            existingUser.Enabled = true;
            existingUser.PasswordHash = passwordHasher.HashPassword(passwordToHash);
            existingUser.MustChangePassword = mustChangePassword;
        }

        if (superAdminRole is not null && existingUser.UserRoles.All(ur => ur.RoleId != superAdminRole.Id))
        {
            db.PanelUserRoles.Add(new PanelUserRoleEntity
            {
                UserId = existingUser.Id,
                RoleId = superAdminRole.Id
            });
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "system",
            ActorName = "bootstrap",
            Action = "auth.bootstrap_admin",
            Target = existingUser.Username,
            Result = "ok",
            DetailJson = $"{{\"mustChangePassword\":{(mustChangePassword ? "true" : "false")}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (generatedPassword is not null)
        {
            _logger.LogWarning(
                "[SECURITY BOOTSTRAP] Created initial super_admin '{AdminUsername}' with one-time password: {BootstrapPassword} (MustChangePassword=true on first Web login).",
                existingUser.Username,
                generatedPassword);
        }
        else
        {
            _logger.LogInformation(
                "Initialized super_admin '{AdminUsername}' from bootstrap configuration.",
                existingUser.Username);
        }

        return new BootstrapAdminResult(true, existingUser.Username, generatedPassword, mustChangePassword);
    }

    public async Task<DatabaseHealthSnapshot> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await using OperationalDbContext db = CreateDbContext();
            bool canConnect = await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            string journalMode = _options.DbProvider == OperationalDbProvider.Postgres ? "WAL (PostgreSQL)" : "unknown";

            if (_options.DbProvider == OperationalDbProvider.Sqlite && canConnect)
            {
                DbConnection conn = db.Database.GetDbConnection();
                bool openedHere = conn.State != System.Data.ConnectionState.Open;
                if (openedHere)
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using DbCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA journal_mode;";
                    object? scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    journalMode = scalar?.ToString()?.ToUpperInvariant() ?? "WAL";
                }
                finally
                {
                    if (openedHere)
                        await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            int activeUsers = await db.PanelUsers.CountAsync(u => u.Enabled, cancellationToken).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            int activeBans = await db.Bans.CountAsync(b => b.ExpiresAt == null || b.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
            long totalReplays = await db.DuelRecords.LongCountAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return new DatabaseHealthSnapshot(
                _options.DbProvider.ToString(),
                canConnect,
                Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                journalMode,
                activeUsers,
                activeBans,
                totalReplays,
                null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Operational database health check failed.");
            return new DatabaseHealthSnapshot(
                _options.DbProvider.ToString(),
                false,
                Math.Round(sw.Elapsed.TotalMilliseconds, 2),
                "error",
                0,
                0,
                0,
                ex.Message);
        }
    }

    private static string? ResolveBootstrapSetting(string? explicitValue, string envVarName)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
            return explicitValue.Trim();

        string? env = Environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    private static string NormalizeConnectionString(PersistenceOptions options)
    {
        if (options.DbProvider != OperationalDbProvider.Sqlite)
            return options.ConnectionString;

        SqliteConnectionStringBuilder builder = new(options.ConnectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) &&
            !string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) &&
            builder.Mode != SqliteOpenMode.Memory)
        {
            string fullPath = Path.GetFullPath(builder.DataSource);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            builder.DataSource = fullPath;
        }

        // 关闭文件句柄连接池驻留，避免临时目录清理与备份在 Windows 上出现文件锁占用
        if (!options.ConnectionString.Contains("Pooling=", StringComparison.OrdinalIgnoreCase))
            builder.Pooling = false;

        if (builder.DefaultTimeout <= 0)
            builder.DefaultTimeout = 5;

        return builder.ToString();
    }

    private static DbContextOptions<OperationalDbContext> BuildDbContextOptions(
        OperationalDbProvider provider,
        string connectionString)
    {
        DbContextOptionsBuilder<OperationalDbContext> builder = new();
        if (provider == OperationalDbProvider.Postgres)
        {
            builder.UseNpgsql(connectionString);
        }
        else
        {
            builder.UseSqlite(connectionString);
            builder.AddInterceptors(SqliteWalConnectionInterceptor.Instance);
        }

        return builder.Options;
    }

    private sealed class SqliteWalConnectionInterceptor : DbConnectionInterceptor
    {
        public static SqliteWalConnectionInterceptor Instance { get; } = new();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyPragmas(connection);
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void ApplyPragmas(DbConnection connection)
        {
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
    }
}
