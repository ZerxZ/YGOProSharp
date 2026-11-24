using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

public sealed record AuthenticatedPrincipal(
    Guid SessionId,
    Guid UserId,
    string Username,
    string DisplayName,
    bool MustChangePassword,
    string CsrfToken,
    IReadOnlyList<string> RoleCodes,
    IReadOnlySet<string> EffectivePermissions)
{
    public bool HasPermission(string permission) => RbacPolicyEvaluator.HasPermission(EffectivePermissions, permission);

    public bool IsStreamerOnly =>
        RoleCodes.Count > 0 &&
        RoleCodes.All(static r => string.Equals(r, BuiltInRoleCodes.Streamer, StringComparison.OrdinalIgnoreCase)) &&
        !HasPermission(RbacPermissions.RoomViewIp);

    public bool CanViewIp => HasPermission(RbacPermissions.RoomViewIp) && !IsStreamerOnly;
}

public sealed record LoginResult(
    bool Success,
    string StatusCode,
    string Message,
    string? SessionToken,
    string? CsrfToken,
    DateTime? ExpiresAt,
    int? RemainingAttempts,
    int? LockedSeconds,
    AuthenticatedPrincipal? Principal);

public sealed record ValidatedMcpToken(
    Guid TokenId,
    Guid OwnerUserId,
    string OwnerUsername,
    string Name,
    string McpRole,
    IReadOnlyList<string> AllowedRooms,
    int MaxSessions,
    bool RequireApproval);

public sealed record IssuedMcpTokenResult(
    McpTokenEntity Entity,
    string RawToken);

public sealed record ActiveBanRule(
    Guid Id,
    string TargetType,
    string TargetValue,
    string Reason,
    DateTime? ExpiresAt,
    string CreatedByName,
    DateTime CreatedAt);

public sealed record SaveDuelRecordRequest(
    string RoomName,
    string RoomIdentifier,
    int Mode,
    int Region,
    int MasterRule,
    int StartLp,
    string HostInfoJson,
    string RandomDeckMode,
    bool IsSuddenDeath,
    int DuelCount,
    int TurnCount,
    int WinnerTeam,
    int WinReason,
    uint Seed,
    DateTime StartedAt,
    DateTime EndedAt,
    byte[]? YrpBytes,
    byte[]? ObserverPacketFrames,
    IReadOnlyList<SaveDuelPlayerSlot> Players);

public sealed record SaveDuelPlayerSlot(
    int SeatIndex,
    int TeamId,
    string PlayerName,
    string RealName,
    string SeatType,
    string? ClientIp,
    string? StartDeckYdk,
    string? IngameDeckYdk,
    bool IsWinner);

/// <summary>
/// 运营数据与多级缓存统一业务门面（认证会话、五级 RBAC、权限不放大校验、MCP Token、封禁、战报云录像、赛事交牌与审计）。
/// </summary>
public sealed class OperationalStore : IAsyncDisposable, IDisposable
{
    private readonly ILogger<OperationalStore> _logger = AppLog.CreateLogger<OperationalStore>();
    private readonly PersistenceOptions _options;
    private readonly Argon2PasswordHasher _passwordHasher;
    private readonly OperationalDatabaseManager _dbManager;
    private readonly OperationalCacheService _cache;
    private bool _disposed;

    public OperationalStore(PersistenceOptions? options = null)
    {
        _options = options ?? new PersistenceOptions();
        _passwordHasher = new Argon2PasswordHasher(
            _options.Argon2MemoryKb,
            _options.Argon2Iterations,
            _options.Argon2Parallelism);
        _dbManager = new OperationalDatabaseManager(_options);
        _cache = new OperationalCacheService(_options);
    }

    public PersistenceOptions Options => _options;
    public OperationalDatabaseManager Database => _dbManager;
    public OperationalCacheService Cache => _cache;
    public Argon2PasswordHasher PasswordHasher => _passwordHasher;
    public BootstrapAdminResult? LastBootstrapResult { get; private set; }

    public async Task<BootstrapAdminResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetFullPath(_options.ReplayDirectory));
        BootstrapAdminResult result = await _dbManager.InitializeAndSeedAsync(_passwordHasher, cancellationToken).ConfigureAwait(false);
        LastBootstrapResult = result;
        return result;
    }

    // ==================== 1. 面板登录、会话校验与改密 ====================

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        string normalizedIp = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim();
        RateLimitDecision rl = await _cache.CheckSlidingWindowAsync(
            $"login:{normalizedIp}",
            _options.LoginRateLimitCount,
            TimeSpan.FromSeconds(_options.LoginRateLimitWindowSeconds),
            cancellationToken).ConfigureAwait(false);

        if (!rl.Allowed)
        {
            await AppendAuditAsync("anonymous", null, username ?? "unknown", "auth.login", null, username, ip, "rate_limited",
                $"{{\"retryAfterSeconds\":{rl.RetryAfterSeconds}}}", cancellationToken).ConfigureAwait(false);
            return new LoginResult(false, "rate_limited", $"登录请求过于频繁，请在 {rl.RetryAfterSeconds} 秒后重试。", null, null, null, 0, rl.RetryAfterSeconds, null);
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new LoginResult(false, "invalid_input", "请输入账号和密码。", null, null, null, null, null, null);

        string normalizedUser = username.Trim().ToLowerInvariant();
        DateTime now = DateTime.UtcNow;

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelUserEntity? user = await db.PanelUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUser, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            await AppendAuditAsync("anonymous", null, normalizedUser, "auth.login", null, normalizedUser, ip, "bad_password", null, cancellationToken).ConfigureAwait(false);
            return new LoginResult(false, "bad_credentials", "用户名或密码错误。", null, null, null, null, null, null);
        }

        if (!user.Enabled)
        {
            await AppendAuditAsync("panel_user", user.Id.ToString(), user.Username, "auth.login", null, user.Username, ip, "disabled", null, cancellationToken).ConfigureAwait(false);
            return new LoginResult(false, "account_disabled", "该账号已被停用，请联系超级管理员。", null, null, null, 0, null, null);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
        {
            int lockedSecs = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - now).TotalSeconds));
            await AppendAuditAsync("panel_user", user.Id.ToString(), user.Username, "auth.login", null, user.Username, ip, "locked",
                $"{{\"lockedSeconds\":{lockedSecs}}}", cancellationToken).ConfigureAwait(false);
            return new LoginResult(false, "account_locked", $"密码连续错误次数过多，账号已锁定，剩余 {lockedSecs} 秒。", null, null, null, 0, lockedSecs, null);
        }

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            int maxFailures = Math.Max(1, _options.MaxLoginFailures);
            int remaining = Math.Max(0, maxFailures - user.FailedLoginCount);
            int? lockedSeconds = null;
            string resultTag = "bad_password";

            if (user.FailedLoginCount >= maxFailures)
            {
                TimeSpan lockDuration = TimeSpan.FromMinutes(Math.Max(1, _options.LockoutMinutes));
                user.LockedUntil = now.Add(lockDuration);
                lockedSeconds = (int)lockDuration.TotalSeconds;
                resultTag = "locked";
            }

            db.AuditLogs.Add(new AuditLogEntity
            {
                ActorType = "panel_user",
                ActorId = user.Id.ToString(),
                ActorName = user.Username,
                Action = "auth.login",
                Target = user.Username,
                Ip = ip,
                Result = resultTag,
                DetailJson = $"{{\"failedCount\":{user.FailedLoginCount},\"remaining\":{remaining}}}"
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            string errMsg = lockedSeconds.HasValue
                ? $"密码错误已达 {maxFailures} 次上限，账号已自动锁定 {_options.LockoutMinutes} 分钟。"
                : $"用户名或密码错误（剩余可尝试 {remaining} 次）。";
            return new LoginResult(false, resultTag, errMsg, null, null, null, remaining, lockedSeconds, null);
        }

        // 登录成功：清空失败计数并签发会话
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        user.LastLoginIp = ip;

        (string rawToken, string prefix, string tokenSha256) = TokenCrypto.GeneratePrefixedToken("ygp_sess");
        string csrfToken = TokenCrypto.GenerateNonce(24);
        DateTime expiresAt = now.AddHours(Math.Max(1, _options.SessionTtlHours));

        PanelSessionEntity session = new()
        {
            UserId = user.Id,
            TokenPrefix = prefix,
            TokenSha256 = tokenSha256,
            CsrfToken = csrfToken,
            Ip = ip,
            UserAgent = userAgent is { Length: > 500 } ? userAgent[..500] : userAgent,
            ExpiresAt = expiresAt,
            LastSeenAt = now
        };
        db.PanelSessions.Add(session);

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = user.Id.ToString(),
            ActorName = user.Username,
            Action = "auth.login",
            Target = session.Id.ToString(),
            Ip = ip,
            Result = "ok",
            DetailJson = $"{{\"mustChangePassword\":{(user.MustChangePassword ? "true" : "false")}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        AuthenticatedPrincipal principal = BuildPrincipal(session, user);
        return new LoginResult(true, "ok", "登录成功。", rawToken, csrfToken, expiresAt, null, null, principal);
    }

    public async Task<AuthenticatedPrincipal?> ValidateSessionAsync(
        string? rawSessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
            return null;

        string candidateSha = TokenCrypto.ComputeSha256Hex(rawSessionToken.Trim());
        CachedSessionDto? cached = await _cache.GetOrCreateAsync<CachedSessionDto?>(
            "auth",
            $"sess:{candidateSha}",
            async ct =>
            {
                await using OperationalDbContext db = _dbManager.CreateDbContext();
                DateTime now = DateTime.UtcNow;
                PanelSessionEntity? session = await db.PanelSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TokenSha256 == candidateSha, ct)
                    .ConfigureAwait(false);

                if (session is null || session.RevokedAt != null || session.ExpiresAt <= now)
                    return null;

                if (!TokenCrypto.FixedTimeEqualsSha256Hex(candidateSha, session.TokenSha256))
                    return null;

                PanelUserEntity? user = await db.PanelUsers
                    .AsNoTracking()
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Id == session.UserId, ct)
                    .ConfigureAwait(false);

                if (user is null || !user.Enabled)
                    return null;

                AuthenticatedPrincipal built = BuildPrincipal(session, user);
                return new CachedSessionDto(
                    built.SessionId,
                    built.UserId,
                    session.TokenSha256,
                    built.Username,
                    built.DisplayName,
                    built.MustChangePassword,
                    built.CsrfToken,
                    session.ExpiresAt,
                    built.RoleCodes.ToArray(),
                    built.EffectivePermissions.ToArray());
            },
            ttl: TimeSpan.FromMinutes(15),
            l1Ttl: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (cached is null || cached.ExpiresAt <= DateTime.UtcNow)
            return null;

        if (!TokenCrypto.FixedTimeEqualsSha256Hex(candidateSha, cached.TokenSha256))
            return null;

        return new AuthenticatedPrincipal(
            cached.SessionId,
            cached.UserId,
            cached.Username,
            cached.DisplayName,
            cached.MustChangePassword,
            cached.CsrfToken,
            cached.RoleCodes,
            new HashSet<string>(cached.Permissions, StringComparer.OrdinalIgnoreCase));
    }

    public async Task LogoutSessionAsync(
        string? rawSessionToken,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSessionToken))
            return;

        string sha = TokenCrypto.ComputeSha256Hex(rawSessionToken.Trim());
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelSessionEntity? session = await db.PanelSessions
            .FirstOrDefaultAsync(s => s.TokenSha256 == sha && s.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (session is not null)
        {
            session.RevokedAt = DateTime.UtcNow;
            db.AuditLogs.Add(new AuditLogEntity
            {
                ActorType = "panel_user",
                ActorId = session.UserId.ToString(),
                ActorName = session.UserId.ToString(),
                Action = "auth.logout",
                Target = session.Id.ToString(),
                Ip = ip,
                Result = "ok"
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await _cache.RemoveAsync("auth", $"sess:{sha}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message)> ChangeOwnPasswordAsync(
        Guid userId,
        Guid currentSessionId,
        string currentPassword,
        string newPassword,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelUserEntity? user = await db.PanelUsers
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return (false, "用户不存在。");

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            await AppendAuditAsync("panel_user", user.Id.ToString(), user.Username, "auth.change_password", null, user.Username, ip, "bad_password", null, cancellationToken).ConfigureAwait(false);
            return (false, "当前原密码不正确。");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            return (false, "新密码不能与当前密码相同。");

        (bool valid, string? strengthErr) = Argon2PasswordHasher.ValidatePasswordStrength(newPassword, user.Username);
        if (!valid)
            return (false, strengthErr ?? "新密码不符合安全要求。");

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        // 吊销该账号除当前会话外的所有其他活跃会话，并刷新当前会话缓存
        DateTime now = DateTime.UtcNow;
        List<PanelSessionEntity> otherSessions = await db.PanelSessions
            .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (PanelSessionEntity other in otherSessions)
            other.RevokedAt = now;

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = user.Id.ToString(),
            ActorName = user.Username,
            Action = "auth.change_password",
            Target = user.Username,
            Ip = ip,
            Result = "ok",
            DetailJson = $"{{\"revokedOtherSessions\":{otherSessions.Count}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, "密码修改成功，已自动注销其他设备会话。");
    }

    // ==================== 2. 用户、角色与权限不放大管理 ====================

    public async Task<IReadOnlyList<PanelUserEntity>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.PanelUsers
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PanelRoleEntity>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.PanelRoles
            .AsNoTracking()
            .OrderByDescending(r => r.IsBuiltIn)
            .ThenBy(r => r.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message, PanelUserEntity? User, string? GeneratedPassword)> CreateUserAsync(
        AuthenticatedPrincipal actor,
        string username,
        string displayName,
        string? initialPassword,
        IReadOnlyList<string> roleCodes,
        UserPermissionsOverride? permissionsOverride,
        bool mustChangePassword,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.UserManage))
            return (false, "缺少 user:manage 权限。", null, null);

        if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 2)
            return (false, "用户名长度至少 2 个字符。", null, null);

        string trimmedUsername = username.Trim();
        string normalizedUsername = trimmedUsername.ToLowerInvariant();

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        if (await db.PanelUsers.AnyAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken).ConfigureAwait(false))
            return (false, $"用户名 '{trimmedUsername}' 已存在。", null, null);

        List<PanelRoleEntity> targetRoles = await db.PanelRoles
            .Where(r => roleCodes.Contains(r.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (targetRoles.Count == 0)
            return (false, "至少需要为用户指定一个有效角色。", null, null);

        // 权限不放大铁律校验
        HashSet<string> targetEffective = RbacPolicyEvaluator.ComputeEffectivePermissions(
            targetRoles.Select(static r => RbacPolicyEvaluator.ParsePermissionsJson(r.PermissionsJson)),
            permissionsOverride);

        if (!RbacPolicyEvaluator.IsSubsetOfActorPermissions(actor.EffectivePermissions, targetEffective))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "user.create", RbacPermissions.UserManage, trimmedUsername, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, "权限不放大保护：目标用户的角色或附加权限超出了您自身拥有的有效权限范围。", null, null);
        }

        string? generatedPassword = null;
        string rawPassword;
        bool forceChange = mustChangePassword;

        if (string.IsNullOrWhiteSpace(initialPassword))
        {
            generatedPassword = TokenCrypto.GenerateBootstrapPassword(20);
            rawPassword = generatedPassword;
            forceChange = true;
        }
        else
        {
            (bool valid, string? err) = Argon2PasswordHasher.ValidatePasswordStrength(initialPassword, trimmedUsername);
            if (!valid)
                return (false, err ?? "初始密码不符合安全要求。", null, null);
            rawPassword = initialPassword;
        }

        PanelUserEntity created = new()
        {
            Username = trimmedUsername,
            NormalizedUsername = normalizedUsername,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedUsername : displayName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(rawPassword),
            Enabled = true,
            MustChangePassword = forceChange,
            PermissionsOverrideJson = RbacPolicyEvaluator.SerializeOverride(permissionsOverride)
        };
        db.PanelUsers.Add(created);

        foreach (PanelRoleEntity role in targetRoles)
        {
            db.PanelUserRoles.Add(new PanelUserRoleEntity
            {
                UserId = created.Id,
                RoleId = role.Id
            });
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "user.create",
            PermissionChecked = RbacPermissions.UserManage,
            Target = created.Username,
            Ip = ip,
            Result = "ok",
            DetailJson = JsonSerializer.Serialize(new { roles = targetRoles.Select(r => r.Code).ToArray(), forceChange })
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, "用户创建成功。", created, generatedPassword);
    }

    public async Task<(bool Success, string Message)> UpdateUserAsync(
        AuthenticatedPrincipal actor,
        Guid targetUserId,
        string? displayName,
        bool? enabled,
        bool unlockAccount,
        IReadOnlyList<string>? roleCodes,
        UserPermissionsOverride? permissionsOverride,
        bool updateOverride,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.UserManage))
            return (false, "缺少 user:manage 权限。");

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelUserEntity? user = await db.PanelUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return (false, "目标用户不存在。");

        if (enabled == false && user.Id == actor.UserId)
            return (false, "不能停用当前登录的自身账号。");

        List<PanelRoleEntity> nextRoles = roleCodes is not null
            ? await db.PanelRoles.Where(r => roleCodes.Contains(r.Code)).ToListAsync(cancellationToken).ConfigureAwait(false)
            : user.UserRoles.Select(static ur => ur.Role).ToList();

        if (nextRoles.Count == 0)
            return (false, "用户至少需要保留一个角色。");

        UserPermissionsOverride nextOverride = updateOverride
            ? (permissionsOverride ?? UserPermissionsOverride.Empty)
            : RbacPolicyEvaluator.ParseOverrideJson(user.PermissionsOverrideJson);

        HashSet<string> nextEffective = RbacPolicyEvaluator.ComputeEffectivePermissions(
            nextRoles.Select(static r => RbacPolicyEvaluator.ParsePermissionsJson(r.PermissionsJson)),
            nextOverride);

        if (!RbacPolicyEvaluator.IsSubsetOfActorPermissions(actor.EffectivePermissions, nextEffective))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "user.update", RbacPermissions.UserManage, user.Username, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, "权限不放大保护：无法授予高于自身有效权限的角色或权限点。");
        }

        if (!string.IsNullOrWhiteSpace(displayName))
            user.DisplayName = displayName.Trim();

        if (enabled.HasValue)
            user.Enabled = enabled.Value;

        if (unlockAccount)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
        }

        if (updateOverride)
            user.PermissionsOverrideJson = RbacPolicyEvaluator.SerializeOverride(nextOverride);

        if (roleCodes is not null)
        {
            db.PanelUserRoles.RemoveRange(user.UserRoles);
            foreach (PanelRoleEntity role in nextRoles)
            {
                db.PanelUserRoles.Add(new PanelUserRoleEntity
                {
                    UserId = user.Id,
                    RoleId = role.Id
                });
            }
        }

        if (enabled == false)
        {
            DateTime now = DateTime.UtcNow;
            List<PanelSessionEntity> sessions = await db.PanelSessions
                .Where(s => s.UserId == user.Id && s.RevokedAt == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (PanelSessionEntity s in sessions)
                s.RevokedAt = now;

            List<McpTokenEntity> tokens = await db.McpTokens
                .Where(t => t.OwnerUserId == user.Id && t.RevokedAt == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (McpTokenEntity t in tokens)
                t.RevokedAt = now;
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "user.update",
            PermissionChecked = RbacPermissions.UserManage,
            Target = user.Username,
            Ip = ip,
            Result = "ok"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, "用户信息已更新。");
    }

    public async Task<(bool Success, string Message, string? OneTimePassword)> ResetUserPasswordAsync(
        AuthenticatedPrincipal actor,
        Guid targetUserId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.UserManage))
            return (false, "缺少 user:manage 权限。", null);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelUserEntity? user = await db.PanelUsers
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return (false, "目标用户不存在。", null);

        string tempPassword = TokenCrypto.GenerateBootstrapPassword(20);
        user.PasswordHash = _passwordHasher.HashPassword(tempPassword);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        DateTime now = DateTime.UtcNow;
        List<PanelSessionEntity> sessions = await db.PanelSessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (PanelSessionEntity s in sessions)
            s.RevokedAt = now;

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "user.reset_password",
            PermissionChecked = RbacPermissions.UserManage,
            Target = user.Username,
            Ip = ip,
            Result = "ok",
            DetailJson = $"{{\"revokedSessions\":{sessions.Count}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, "已重置一次性密码并强制下次登录改密。", tempPassword);
    }

    public async Task<IReadOnlyList<PanelSessionEntity>> ListSessionsAsync(
        Guid? filterUserId,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        DateTime now = DateTime.UtcNow;
        IQueryable<PanelSessionEntity> query = db.PanelSessions
            .AsNoTracking()
            .Where(s => s.RevokedAt == null && s.ExpiresAt > now);

        if (filterUserId.HasValue)
            query = query.Where(s => s.UserId == filterUserId.Value);

        return await query.OrderByDescending(s => s.LastSeenAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RevokeSessionByIdAsync(
        AuthenticatedPrincipal actor,
        Guid sessionId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelSessionEntity? session = await db.PanelSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
            return false;

        if (session.UserId != actor.UserId && !actor.HasPermission(RbacPermissions.UserManage))
            return false;

        session.RevokedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "session.revoke",
            Target = session.Id.ToString(),
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<(bool Success, string Message, PanelRoleEntity? Role)> UpsertRoleAsync(
        AuthenticatedPrincipal actor,
        string code,
        string name,
        string description,
        IReadOnlyList<string> permissions,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.RoleManage))
            return (false, "缺少 role:manage 权限。", null);

        if (string.IsNullOrWhiteSpace(code))
            return (false, "角色编码不能为空。", null);

        string normalizedCode = code.Trim().ToLowerInvariant();
        if (string.Equals(normalizedCode, BuiltInRoleCodes.SuperAdmin, StringComparison.OrdinalIgnoreCase))
            return (false, "内置 super_admin 角色权限不可修改。", null);

        if (!RbacPolicyEvaluator.IsSubsetOfActorPermissions(actor.EffectivePermissions, permissions))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "role.upsert", RbacPermissions.RoleManage, normalizedCode, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, "权限不放大保护：角色包含超出操作者自身范围的权限码。", null);
        }

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelRoleEntity? role = await db.PanelRoles
            .FirstOrDefaultAsync(r => r.Code == normalizedCode, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            role = new PanelRoleEntity
            {
                Code = normalizedCode,
                Name = string.IsNullOrWhiteSpace(name) ? normalizedCode : name.Trim(),
                Description = description?.Trim() ?? string.Empty,
                IsBuiltIn = false,
                PermissionsJson = RbacPolicyEvaluator.SerializePermissions(permissions)
            };
            db.PanelRoles.Add(role);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(name))
                role.Name = name.Trim();
            if (description is not null)
                role.Description = description.Trim();
            role.PermissionsJson = RbacPolicyEvaluator.SerializePermissions(permissions);
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "role.upsert",
            PermissionChecked = RbacPermissions.RoleManage,
            Target = role.Code,
            Ip = ip,
            Result = "ok"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, "角色配置已保存。", role);
    }

    // ==================== 3. MCP Token 签发（权限不放大校验）与常量时间鉴权 ====================

    public async Task<IReadOnlyList<McpTokenEntity>> ListMcpTokensAsync(
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        bool viewAll = actor.HasPermission(RbacPermissions.McpTokenManageAll) || actor.HasPermission(RbacPermissions.UserManage);
        IQueryable<McpTokenEntity> query = db.McpTokens.AsNoTracking();
        if (!viewAll)
            query = query.Where(t => t.OwnerUserId == actor.UserId);

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message, IssuedMcpTokenResult? Issued)> IssueMcpTokenAsync(
        AuthenticatedPrincipal actor,
        string name,
        string mcpRole,
        IReadOnlyList<string>? allowedRooms,
        int maxSessions,
        bool requireApproval,
        int? expiresInHours,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        string normalizedRole = string.IsNullOrWhiteSpace(mcpRole) ? McpRoleCodes.Player : mcpRole.Trim().ToLowerInvariant();
        if (!McpRoleCodes.All.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
            return (false, $"不支持的 MCP 角色 '{mcpRole}'。", null);

        if (!RbacPolicyEvaluator.CanIssueMcpRole(actor.RoleCodes, actor.EffectivePermissions, normalizedRole))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "mcp_token.issue", RbacPermissions.McpTokenIssueSelf, normalizedRole, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, $"权限不放大保护：当前账号无权签发 '{normalizedRole}' 级别的 MCP Token。", null);
        }

        (string rawToken, string prefix, string sha256) = TokenCrypto.GeneratePrefixedToken($"ygp_mcp_{normalizedRole}");
        string[] cleanRooms = allowedRooms?
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        McpTokenEntity entity = new()
        {
            OwnerUserId = actor.UserId,
            OwnerUsername = actor.Username,
            Name = string.IsNullOrWhiteSpace(name) ? $"{normalizedRole}-{DateTime.UtcNow:MMddHHmm}" : name.Trim(),
            TokenPrefix = prefix,
            TokenSha256 = sha256,
            McpRole = normalizedRole,
            AllowedRoomsJson = cleanRooms.Length == 0 ? null : JsonSerializer.Serialize(cleanRooms),
            MaxSessions = Math.Clamp(maxSessions, 1, 16),
            RequireApproval = requireApproval,
            ExpiresAt = expiresInHours is > 0 ? DateTime.UtcNow.AddHours(expiresInHours.Value) : null
        };

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        db.McpTokens.Add(entity);
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "mcp_token.issue",
            PermissionChecked = RbacPermissions.McpTokenIssueSelf,
            Target = $"{entity.Name} ({entity.McpRole})",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (true, "MCP Token 签发成功（明文仅展示一次，请妥善保存）。", new IssuedMcpTokenResult(entity, rawToken));
    }

    public async Task<ValidatedMcpToken?> ValidateMcpTokenAsync(
        string? rawMcpToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawMcpToken))
            return null;

        string candidateSha = TokenCrypto.ComputeSha256Hex(rawMcpToken.Trim());
        CachedMcpTokenDto? cached = await _cache.GetOrCreateAsync<CachedMcpTokenDto?>(
            "auth",
            $"mcp:{candidateSha}",
            async ct =>
            {
                await using OperationalDbContext db = _dbManager.CreateDbContext();
                DateTime now = DateTime.UtcNow;
                McpTokenEntity? token = await db.McpTokens
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TokenSha256 == candidateSha, ct)
                    .ConfigureAwait(false);

                if (token is null || token.RevokedAt != null || (token.ExpiresAt.HasValue && token.ExpiresAt.Value <= now))
                    return null;

                if (!TokenCrypto.FixedTimeEqualsSha256Hex(candidateSha, token.TokenSha256))
                    return null;

                bool ownerEnabled = await db.PanelUsers
                    .AnyAsync(u => u.Id == token.OwnerUserId && u.Enabled, ct)
                    .ConfigureAwait(false);
                if (!ownerEnabled)
                    return null;

                string[] rooms = string.IsNullOrWhiteSpace(token.AllowedRoomsJson)
                    ? []
                    : (JsonSerializer.Deserialize<string[]>(token.AllowedRoomsJson) ?? []);

                return new CachedMcpTokenDto(
                    token.Id,
                    token.OwnerUserId,
                    token.OwnerUsername,
                    token.TokenSha256,
                    token.Name,
                    token.McpRole,
                    rooms,
                    token.MaxSessions,
                    token.RequireApproval,
                    token.ExpiresAt);
            },
            ttl: TimeSpan.FromMinutes(10),
            l1Ttl: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (cached is null || (cached.ExpiresAt.HasValue && cached.ExpiresAt.Value <= DateTime.UtcNow))
            return null;

        if (!TokenCrypto.FixedTimeEqualsSha256Hex(candidateSha, cached.TokenSha256))
            return null;

        return new ValidatedMcpToken(
            cached.TokenId,
            cached.OwnerUserId,
            cached.OwnerUsername,
            cached.Name,
            cached.McpRole,
            cached.AllowedRooms,
            cached.MaxSessions,
            cached.RequireApproval);
    }

    public async Task<bool> RevokeMcpTokenAsync(
        AuthenticatedPrincipal actor,
        Guid tokenId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        McpTokenEntity? token = await db.McpTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (token is null)
            return false;

        bool canManageAll = actor.HasPermission(RbacPermissions.McpTokenManageAll) || actor.HasPermission(RbacPermissions.UserManage);
        if (token.OwnerUserId != actor.UserId && !canManageAll)
            return false;

        token.RevokedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "mcp_token.revoke",
            Target = token.Id.ToString(),
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.RemoveAsync("auth", $"mcp:{token.TokenSha256}", cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ==================== 4. 封禁表、玩家 vpass 校验与比赛交牌 ====================

    public async Task<IReadOnlyList<ActiveBanRule>> GetActiveBansAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync<IReadOnlyList<ActiveBanRule>>(
            "ban",
            "active_set",
            async ct =>
            {
                await using OperationalDbContext db = _dbManager.CreateDbContext();
                DateTime now = DateTime.UtcNow;
                List<BanEntity> entities = await db.Bans
                    .AsNoTracking()
                    .Where(b => b.ExpiresAt == null || b.ExpiresAt > now)
                    .OrderByDescending(b => b.CreatedAt)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return entities.Select(static b => new ActiveBanRule(
                    b.Id,
                    b.TargetType,
                    b.TargetValue,
                    b.Reason,
                    b.ExpiresAt,
                    b.CreatedByName,
                    b.CreatedAt)).ToArray();
            },
            ttl: TimeSpan.FromMinutes(5),
            l1Ttl: TimeSpan.FromMinutes(2),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActiveBanRule?> CheckBanMatchAsync(
        string? ip,
        string? playerName,
        string? clientKey,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ActiveBanRule> activeBans = await GetActiveBansAsync(cancellationToken).ConfigureAwait(false);
        DateTime now = DateTime.UtcNow;

        for (int i = 0; i < activeBans.Count; i++)
        {
            ActiveBanRule ban = activeBans[i];
            if (ban.ExpiresAt.HasValue && ban.ExpiresAt.Value <= now)
                continue;

            if ((string.Equals(ban.TargetType, "ip", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(ban.TargetType, "cidr", StringComparison.OrdinalIgnoreCase)) &&
                IpPrivacyHelper.MatchesIpOrCidr(ip, ban.TargetType, ban.TargetValue))
            {
                return ban;
            }

            if (string.Equals(ban.TargetType, "name", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(playerName) &&
                string.Equals(playerName.Trim(), ban.TargetValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ban;
            }

            if (string.Equals(ban.TargetType, "client_key", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(clientKey) &&
                string.Equals(clientKey.Trim(), ban.TargetValue.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return ban;
            }
        }

        return null;
    }

    public async Task<(bool Success, string Message, BanEntity? Ban)> CreateBanAsync(
        AuthenticatedPrincipal actor,
        string targetType,
        string targetValue,
        string reason,
        int? durationMinutes,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.BanManage))
            return (false, "缺少 ban:manage 权限。", null);

        string normalizedType = targetType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedType is not ("ip" or "cidr" or "name" or "client_key"))
            return (false, "封禁目标类型必须是 ip / cidr / name / client_key。", null);

        if (string.IsNullOrWhiteSpace(targetValue))
            return (false, "封禁目标值不能为空。", null);

        BanEntity entity = new()
        {
            TargetType = normalizedType,
            TargetValue = targetValue.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? "违反服务器规则" : reason.Trim(),
            ExpiresAt = durationMinutes is > 0 ? DateTime.UtcNow.AddMinutes(durationMinutes.Value) : null,
            CreatedByUserId = actor.UserId,
            CreatedByName = actor.Username
        };

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        db.Bans.Add(entity);
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "ban.create",
            PermissionChecked = RbacPermissions.BanManage,
            Target = $"{entity.TargetType}:{entity.TargetValue}",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("ban", cancellationToken).ConfigureAwait(false);
        return (true, "封禁已生效。", entity);
    }

    public async Task<bool> RemoveBanAsync(
        AuthenticatedPrincipal actor,
        Guid banId,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!actor.HasPermission(RbacPermissions.BanManage))
            return false;

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        BanEntity? ban = await db.Bans.FirstOrDefaultAsync(b => b.Id == banId, cancellationToken).ConfigureAwait(false);
        if (ban is null)
            return false;

        ban.DeletedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "ban.remove",
            PermissionChecked = RbacPermissions.BanManage,
            Target = $"{ban.TargetType}:{ban.TargetValue}",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("ban", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 校验或首次绑定客户端 <c>昵称$密码</c>（<c>vpass</c>）身份，防止他人冒用已受保护的玩家昵称。
    /// </summary>
    public async Task<(bool Allowed, string? RejectionReason)> VerifyPlayerVpassAsync(
        string playerName,
        string? vpass,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return (true, null);

        string normalized = playerName.Trim().ToLowerInvariant();
        string? cachedHash = await _cache.GetOrCreateAsync<string?>(
            "player",
            $"vpass:{normalized}",
            async ct =>
            {
                await using OperationalDbContext db = _dbManager.CreateDbContext();
                PlayerProfileEntity? profile = await db.PlayerProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NormalizedName == normalized, ct)
                    .ConfigureAwait(false);
                return profile?.VpassHash ?? "__NONE__";
            },
            ttl: TimeSpan.FromMinutes(5),
            l1Ttl: TimeSpan.FromMinutes(1),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (cachedHash is null or "__NONE__")
        {
            if (string.IsNullOrWhiteSpace(vpass))
                return (true, null);

            // 首次携带 $密码 进入：自动注册昵称保护档案
            await using OperationalDbContext db = _dbManager.CreateDbContext();
            PlayerProfileEntity? existing = await db.PlayerProfiles
                .FirstOrDefaultAsync(p => p.NormalizedName == normalized, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                db.PlayerProfiles.Add(new PlayerProfileEntity
                {
                    PlayerName = playerName.Trim(),
                    NormalizedName = normalized,
                    VpassHash = _passwordHasher.HashPassword(vpass)
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _cache.RemoveAsync("player", $"vpass:{normalized}", cancellationToken).ConfigureAwait(false);
                return (true, null);
            }
            cachedHash = existing.VpassHash;
        }

        if (string.IsNullOrWhiteSpace(cachedHash) || cachedHash == "__NONE__")
            return (true, null);

        if (string.IsNullOrEmpty(vpass) || !_passwordHasher.VerifyPassword(vpass, cachedHash))
            return (false, $"昵称 '{playerName}' 已受密码保护，请使用 '{playerName}$密码' 格式登录。");

        return (true, null);
    }

    public async Task<IReadOnlyList<TournamentDeckEntity>> ListTournamentDecksAsync(
        string? tournamentTag,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<TournamentDeckEntity> q = db.TournamentDecks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tournamentTag))
        {
            string tag = tournamentTag.Trim();
            q = q.Where(d => d.TournamentTag == tag);
        }
        return await q.OrderByDescending(d => d.UpdatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TournamentDeckEntity> UpsertTournamentDeckAsync(
        AuthenticatedPrincipal actor,
        string tournamentTag,
        string playerName,
        string ydkContent,
        int mainCount,
        int extraCount,
        int sideCount,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        string tag = string.IsNullOrWhiteSpace(tournamentTag) ? "default" : tournamentTag.Trim();
        string normPlayer = playerName.Trim().ToLowerInvariant();
        string hash = TokenCrypto.ComputeSha256Hex(ydkContent.Replace("\r\n", "\n").Trim());

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        TournamentDeckEntity? entity = await db.TournamentDecks
            .FirstOrDefaultAsync(d => d.TournamentTag == tag && d.NormalizedPlayerName == normPlayer, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new TournamentDeckEntity
            {
                TournamentTag = tag,
                PlayerName = playerName.Trim(),
                NormalizedPlayerName = normPlayer,
                YdkContent = ydkContent,
                DeckHash = hash,
                MainCount = mainCount,
                ExtraCount = extraCount,
                SideCount = sideCount
            };
            db.TournamentDecks.Add(entity);
        }
        else
        {
            entity.PlayerName = playerName.Trim();
            entity.YdkContent = ydkContent;
            entity.DeckHash = hash;
            entity.MainCount = mainCount;
            entity.ExtraCount = extraCount;
            entity.SideCount = sideCount;
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = actor.UserId.ToString(),
            ActorName = actor.Username,
            Action = "tournament_deck.upsert",
            PermissionChecked = RbacPermissions.TournamentDeckWrite,
            Target = $"{tag}:{playerName}",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    // ==================== 5. 对局战报落库与云录像 R#<id> ====================

    public async Task<DuelRecordEntity> SaveDuelRecordAsync(
        SaveDuelRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        DuelRecordEntity record = new()
        {
            RoomName = request.RoomName,
            RoomIdentifier = request.RoomIdentifier,
            Mode = request.Mode,
            Region = request.Region,
            MasterRule = request.MasterRule,
            StartLp = request.StartLp,
            HostInfoJson = request.HostInfoJson,
            RandomDeckMode = request.RandomDeckMode,
            IsSuddenDeath = request.IsSuddenDeath,
            DuelCount = request.DuelCount,
            TurnCount = request.TurnCount,
            WinnerTeam = request.WinnerTeam,
            WinReason = request.WinReason,
            Seed = request.Seed,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt
        };

        foreach (SaveDuelPlayerSlot slot in request.Players)
        {
            record.Players.Add(new DuelRecordPlayerEntity
            {
                SeatIndex = slot.SeatIndex,
                TeamId = slot.TeamId,
                PlayerName = slot.PlayerName,
                RealName = slot.RealName,
                SeatType = slot.SeatType,
                ClientIp = slot.ClientIp,
                StartDeckYdk = slot.StartDeckYdk,
                IngameDeckYdk = slot.IngameDeckYdk,
                IsWinner = slot.IsWinner
            });
        }

        db.DuelRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string replayDir = Path.GetFullPath(_options.ReplayDirectory);
        Directory.CreateDirectory(replayDir);

        bool updatedFiles = false;
        if (request.YrpBytes is { Length: > 0 })
        {
            string yrpFileName = $"replay_{record.Id}.yrp";
            string fullYrpPath = Path.Combine(replayDir, yrpFileName);
            await File.WriteAllBytesAsync(fullYrpPath, request.YrpBytes, cancellationToken).ConfigureAwait(false);
            record.YrpPath = yrpFileName;
            updatedFiles = true;
        }

        if (request.ObserverPacketFrames is { Length: > 0 })
        {
            string obsFileName = $"replay_{record.Id}.obs";
            string fullObsPath = Path.Combine(replayDir, obsFileName);
            await File.WriteAllBytesAsync(fullObsPath, request.ObserverPacketFrames, cancellationToken).ConfigureAwait(false);
            record.ObserverFramesPath = obsFileName;
            updatedFiles = true;
        }

        if (updatedFiles)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Saved duel record R#{ReplayId} for room '{RoomName}' (Mode={Mode}, WinnerTeam={WinnerTeam}).", record.Id, record.RoomName, record.Mode, record.WinnerTeam);
        return record;
    }

    public async Task<IReadOnlyList<DuelRecordEntity>> ListDuelRecordsAsync(
        int limit = 50,
        long? beforeId = null,
        string? playerFilter = null,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 200);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<DuelRecordEntity> q = db.DuelRecords
            .AsNoTracking()
            .Include(r => r.Players);

        if (beforeId.HasValue)
            q = q.Where(r => r.Id < beforeId.Value);

        if (!string.IsNullOrWhiteSpace(playerFilter))
        {
            string kw = playerFilter.Trim();
            q = q.Where(r => r.RoomName.Contains(kw) || r.Players.Any(p => p.PlayerName.Contains(kw)));
        }

        return await q.OrderByDescending(r => r.Id).Take(clamped).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DuelRecordEntity?> GetDuelRecordByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DuelRecords
            .AsNoTracking()
            .Include(r => r.Players)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]?> GetReplayYrpBytesAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync<byte[]?>(
            "replay",
            $"yrp:{id}",
            async ct =>
            {
                DuelRecordEntity? record = await GetDuelRecordByIdAsync(id, ct).ConfigureAwait(false);
                if (record is null || string.IsNullOrWhiteSpace(record.YrpPath))
                    return null;

                string fullPath = ResolveReplayFilePath(record.YrpPath);
                return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, ct).ConfigureAwait(false) : null;
            },
            ttl: TimeSpan.FromMinutes(10),
            l1Ttl: TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetReplayObserverFramesAsync(long id, CancellationToken cancellationToken = default)
    {
        DuelRecordEntity? record = await GetDuelRecordByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null || string.IsNullOrWhiteSpace(record.ObserverFramesPath))
            return null;

        string fullPath = ResolveReplayFilePath(record.ObserverFramesPath);
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false) : null;
    }

    // ==================== 6. 全链路审计日志 ====================

    public async Task AppendAuditAsync(
        string actorType,
        string? actorId,
        string actorName,
        string action,
        string? permissionChecked,
        string? target,
        string? ip,
        string result,
        string? detailJson = null,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = actorType,
            ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(actorName) ? "unknown" : actorName,
            Action = action,
            PermissionChecked = permissionChecked,
            Target = target,
            Ip = ip,
            Result = result,
            DetailJson = detailJson
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AuditLogEntity>> QueryAuditLogsAsync(
        int limit = 100,
        string? actorFilter = null,
        string? actionFilter = null,
        string? resultFilter = null,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 500);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<AuditLogEntity> q = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(actorFilter))
            q = q.Where(a => a.ActorName.Contains(actorFilter.Trim()));
        if (!string.IsNullOrWhiteSpace(actionFilter))
            q = q.Where(a => a.Action.Contains(actionFilter.Trim()));
        if (!string.IsNullOrWhiteSpace(resultFilter))
            q = q.Where(a => a.Result == resultFilter.Trim());

        return await q.OrderByDescending(a => a.Id).Take(clamped).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private string ResolveReplayFilePath(string storedRelativeOrFull)
    {
        string replayRoot = Path.GetFullPath(_options.ReplayDirectory);
        string fileName = Path.GetFileName(storedRelativeOrFull);
        return Path.Combine(replayRoot, fileName);
    }

    private static AuthenticatedPrincipal BuildPrincipal(PanelSessionEntity session, PanelUserEntity user)
    {
        List<string> roleCodes = new();
        List<IReadOnlyList<string>> rolePerms = new();

        foreach (PanelUserRoleEntity ur in user.UserRoles)
        {
            if (ur.Role is null)
                continue;
            roleCodes.Add(ur.Role.Code);
            rolePerms.Add(RbacPolicyEvaluator.ParsePermissionsJson(ur.Role.PermissionsJson));
        }

        UserPermissionsOverride userOverride = RbacPolicyEvaluator.ParseOverrideJson(user.PermissionsOverrideJson);
        HashSet<string> effective = RbacPolicyEvaluator.ComputeEffectivePermissions(rolePerms, userOverride);

        return new AuthenticatedPrincipal(
            session.Id,
            user.Id,
            user.Username,
            user.DisplayName,
            user.MustChangePassword,
            session.CsrfToken,
            roleCodes,
            effective);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cache.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        return _cache.DisposeAsync();
    }

    private sealed record CachedSessionDto(
        Guid SessionId,
        Guid UserId,
        string TokenSha256,
        string Username,
        string DisplayName,
        bool MustChangePassword,
        string CsrfToken,
        DateTime ExpiresAt,
        string[] RoleCodes,
        string[] Permissions);

    private sealed record CachedMcpTokenDto(
        Guid TokenId,
        Guid OwnerUserId,
        string OwnerUsername,
        string TokenSha256,
        string Name,
        string McpRole,
        string[] AllowedRooms,
        int MaxSessions,
        bool RequireApproval,
        DateTime? ExpiresAt);
}
