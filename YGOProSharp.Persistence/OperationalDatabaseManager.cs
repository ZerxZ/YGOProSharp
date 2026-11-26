using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

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
/// 负责双引擎 <see cref="OperationalDbContext"/> 构造、EF Core 标准迁移执行、SQLite WAL 初始化、内置角色/超管引导与健康检查。
/// </summary>
public sealed class OperationalDatabaseManager
{
    private const string InitialMigrationId = "20261124000000_InitialOperationalSchema";
    private const string Phase5MigrationId = "20261126000000_AddPhase5EvaluationsReviewsAndMemories";
    private const string EfProductVersion = "10.0.0";

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

    public OperationalDbContext CreateDbContext() => new(_dbContextOptions);

    public async Task<BootstrapAdminResult> InitializeAndSeedAsync(
        Argon2PasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passwordHasher);

        await using OperationalDbContext db = CreateDbContext();
        await ApplyEfMigrationsAsync(db, cancellationToken).ConfigureAwait(false);

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

        // 2. 解析可选显式指定的管理员用户名与密码
        string? explicitAdminUser = ResolveBootstrapSetting(_options.BootstrapAdminUser, "YGOPRO_ADMIN_USER", "YGOPRO_BOOTSTRAP_ADMIN_USER");
        string? configuredPassword = ResolveBootstrapSetting(_options.BootstrapAdminPassword, "YGOPRO_ADMIN_PASSWORD", "YGOPRO_BOOTSTRAP_ADMIN_PASSWORD");

        if (!string.IsNullOrWhiteSpace(explicitAdminUser) && explicitAdminUser.Trim().Length < 2)
            throw new ArgumentException("自定义管理员用户名长度至少需要 2 个字符。");

        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            (bool pwValid, string? pwErr) = Argon2PasswordHasher.ValidatePasswordStrength(configuredPassword, explicitAdminUser);
            if (!pwValid)
                throw new ArgumentException(pwErr ?? "自定义管理员密码不符合要求。");
        }

        bool hasSuperAdmin = await db.PanelUserRoles
            .AnyAsync(ur => ur.Role.Code == BuiltInRoleCodes.SuperAdmin && ur.User.Enabled, cancellationToken)
            .ConfigureAwait(false);

        if (hasSuperAdmin)
        {
            PanelUserEntity firstAdmin = await db.PanelUsers
                .Include(u => u.UserRoles)
                .Where(u => u.Enabled && u.UserRoles.Any(ur => ur.Role.Code == BuiltInRoleCodes.SuperAdmin))
                .OrderBy(u => u.CreatedAt)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(explicitAdminUser) && string.IsNullOrWhiteSpace(configuredPassword))
                return new BootstrapAdminResult(false, firstAdmin.Username, null, firstAdmin.MustChangePassword);

            PanelUserEntity targetAdmin = firstAdmin;
            if (!string.IsNullOrWhiteSpace(explicitAdminUser))
            {
                string trimmedTarget = explicitAdminUser.Trim();
                string normTarget = trimmedTarget.ToLowerInvariant();
                PanelUserEntity? matchedByName = await db.PanelUsers
                    .Include(u => u.UserRoles)
                    .FirstOrDefaultAsync(u => u.NormalizedUsername == normTarget, cancellationToken)
                    .ConfigureAwait(false);

                if (matchedByName is not null && matchedByName.Id != firstAdmin.Id)
                {
                    targetAdmin = matchedByName;
                    targetAdmin.Enabled = true;
                    if (superAdminRole is not null && targetAdmin.UserRoles.All(ur => ur.RoleId != superAdminRole.Id))
                    {
                        db.PanelUserRoles.Add(new PanelUserRoleEntity
                        {
                            UserId = targetAdmin.Id,
                            RoleId = superAdminRole.Id
                        });
                    }
                }
                else
                {
                    string previousUsername = targetAdmin.Username;
                    targetAdmin.Username = trimmedTarget;
                    targetAdmin.NormalizedUsername = normTarget;
                    if (string.Equals(targetAdmin.DisplayName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(targetAdmin.DisplayName, previousUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        targetAdmin.DisplayName = trimmedTarget;
                    }

                    List<McpTokenEntity> ownedTokens = await db.McpTokens
                        .Where(t => t.OwnerUserId == targetAdmin.Id)
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (McpTokenEntity t in ownedTokens)
                        t.OwnerUsername = trimmedTarget;
                }
            }

            if (!string.IsNullOrWhiteSpace(configuredPassword))
            {
                targetAdmin.PasswordHash = passwordHasher.HashPassword(configuredPassword);
                targetAdmin.MustChangePassword = false;
                targetAdmin.FailedLoginCount = 0;
                targetAdmin.LockedUntil = null;
            }

            db.AuditLogs.Add(new AuditLogEntity
            {
                ActorType = "system",
                ActorName = "bootstrap",
                Action = "auth.configure_admin",
                Target = targetAdmin.Username,
                Result = "ok",
                DetailJson = $"{{\"passwordUpdated\":{(!string.IsNullOrWhiteSpace(configuredPassword) ? "true" : "false")}}}"
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Applied custom super_admin configuration for '{AdminUsername}' (PasswordUpdated={PasswordUpdated}).",
                targetAdmin.Username,
                !string.IsNullOrWhiteSpace(configuredPassword));

            return new BootstrapAdminResult(false, targetAdmin.Username, null, targetAdmin.MustChangePassword);
        }

        string adminUsername = explicitAdminUser ?? "admin";
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

    private static async Task ApplyEfMigrationsAsync(OperationalDbContext db, CancellationToken ct)
    {
        string[] applied = (await db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToArray();
        if (applied.Length == 0 && await TableExistsAsync(db, "panel_users", ct).ConfigureAwait(false))
        {
            // 兼容早期通过 EnsureCreated 建立的存量库：写入标准 HistoryRepository 基线后再运行增量迁移
            IHistoryRepository history = db.GetService<IHistoryRepository>();
            string createHistorySql = history.GetCreateIfNotExistsScript();
            if (!string.IsNullOrWhiteSpace(createHistorySql))
                await db.Database.ExecuteSqlRawAsync(createHistorySql, ct).ConfigureAwait(false);

            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(InitialMigrationId, EfProductVersion)),
                ct).ConfigureAwait(false);

            if (await TableExistsAsync(db, "deck_evaluations", ct).ConfigureAwait(false))
            {
                await db.Database.ExecuteSqlRawAsync(
                    history.GetInsertScript(new HistoryRow(Phase5MigrationId, EfProductVersion)),
                    ct).ConfigureAwait(false);
            }
        }

        await db.Database.MigrateAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> TableExistsAsync(OperationalDbContext db, string tableName, CancellationToken ct)
    {
        DbConnection conn = db.Database.GetDbConnection();
        bool opened = conn.State != System.Data.ConnectionState.Open;
        if (opened)
            await conn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = db.Database.IsSqlite()
                ? $"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{tableName}' LIMIT 1;"
                : $"SELECT 1 FROM information_schema.tables WHERE table_name = '{tableName}' LIMIT 1;";
            object? res = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return res is not null && res != DBNull.Value;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (opened)
                await conn.CloseAsync().ConfigureAwait(false);
        }
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

    private static string? ResolveBootstrapSetting(string? explicitValue, params string[] envVarNames)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
            return explicitValue.Trim();

        foreach (string name in envVarNames)
        {
            string? env = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();
        }

        return null;
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
        builder.ConfigureWarnings(static w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

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
            using DbCommand cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
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
    }
}
