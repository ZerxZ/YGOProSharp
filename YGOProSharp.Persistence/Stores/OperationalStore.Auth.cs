using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

public sealed partial class OperationalStore
{
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

    /// <summary>
    /// 面向前台玩家面板的自助账号注册（固定分配 <c>ai_operator</c> 玩家角色，禁止越权，注册后自动创建登录会话）。
    /// </summary>
    public async Task<LoginResult> RegisterPlayerAccountAsync(
        string username,
        string password,
        string? displayName,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        string normalizedIp = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim();
        RateLimitDecision rl = await _cache.CheckSlidingWindowAsync(
            $"register:{normalizedIp}",
            limit: 8,
            window: TimeSpan.FromMinutes(10),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!rl.Allowed)
        {
            await AppendAuditAsync("anonymous", null, username ?? "unknown", "auth.register", null, username, ip, "rate_limited",
                $"{{\"retryAfterSeconds\":{rl.RetryAfterSeconds}}}", cancellationToken).ConfigureAwait(false);
            return new LoginResult(false, "rate_limited", $"注册请求过于频繁，请在 {rl.RetryAfterSeconds} 秒后重试。", null, null, null, 0, rl.RetryAfterSeconds, null);
        }

        string trimmedUsername = username?.Trim() ?? string.Empty;
        if (trimmedUsername.Length < 2 || trimmedUsername.Length > 32)
            return new LoginResult(false, "invalid_username", "用户名长度需在 2 到 32 个字符之间。", null, null, null, null, null, null);

        if (trimmedUsername.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '$' or '#' or ':' or '/' or '\\'))
            return new LoginResult(false, "invalid_username", "用户名不能包含空格或 $ # / \\ : 等特殊分隔符。", null, null, null, null, null, null);

        (bool pwValid, string? pwError) = Argon2PasswordHasher.ValidatePasswordStrength(password, trimmedUsername);
        if (!pwValid)
            return new LoginResult(false, "weak_password", pwError ?? $"密码长度至少需要 {Argon2PasswordHasher.MinPasswordLength} 位字符。", null, null, null, null, null, null);

        string normalizedUser = trimmedUsername.ToLowerInvariant();
        await using OperationalDbContext db = _dbManager.CreateDbContext();

        if (await db.PanelUsers.AnyAsync(u => u.NormalizedUsername == normalizedUser, cancellationToken).ConfigureAwait(false))
            return new LoginResult(false, "username_taken", $"用户名 '{trimmedUsername}' 已被注册，请换一个名称。", null, null, null, null, null, null);

        PanelRoleEntity? operatorRole = await db.PanelRoles
            .FirstOrDefaultAsync(r => r.Code == BuiltInRoleCodes.AiOperator, cancellationToken)
            .ConfigureAwait(false);

        if (operatorRole is null)
            return new LoginResult(false, "role_missing", "系统尚未完成角色初始化，请稍后再试。", null, null, null, null, null, null);

        DateTime now = DateTime.UtcNow;
        PanelUserEntity created = new()
        {
            Username = trimmedUsername,
            NormalizedUsername = normalizedUser,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? trimmedUsername : displayName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(password),
            Enabled = true,
            MustChangePassword = false,
            LastLoginAt = now,
            LastLoginIp = ip
        };
        db.PanelUsers.Add(created);

        PanelUserRoleEntity userRole = new()
        {
            UserId = created.Id,
            User = created,
            RoleId = operatorRole.Id,
            Role = operatorRole
        };
        created.UserRoles.Add(userRole);
        db.PanelUserRoles.Add(userRole);

        (string rawToken, string prefix, string tokenSha256) = TokenCrypto.GeneratePrefixedToken("ygp_sess");
        string csrfToken = TokenCrypto.GenerateNonce(24);
        DateTime expiresAt = now.AddHours(Math.Max(1, _options.SessionTtlHours));

        PanelSessionEntity session = new()
        {
            UserId = created.Id,
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
            ActorId = created.Id.ToString(),
            ActorName = created.Username,
            Action = "auth.register",
            Target = created.Username,
            Ip = ip,
            Result = "ok",
            DetailJson = "{\"role\":\"ai_operator\"}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        AuthenticatedPrincipal principal = BuildPrincipal(session, created);
        return new LoginResult(true, "ok", $"账号 '{created.Username}' 注册成功，已自动登录！", rawToken, csrfToken, expiresAt, null, null, principal);
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
        string? newPassword,
        string? ip,
        CancellationToken cancellationToken = default,
        string? newUsername = null,
        string? newDisplayName = null)
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

        bool hasNewUsername = !string.IsNullOrWhiteSpace(newUsername);
        bool hasNewDisplayName = !string.IsNullOrWhiteSpace(newDisplayName);
        bool hasNewPassword = !string.IsNullOrWhiteSpace(newPassword);

        if (!user.MustChangePassword && !hasNewUsername && !hasNewDisplayName && !hasNewPassword)
            return (false, $"请填写新的自定义用户名或新密码（密码至少 {Argon2PasswordHasher.MinPasswordLength} 位字符）。");

        if (hasNewUsername)
        {
            string trimmedUser = newUsername!.Trim();
            if (trimmedUser.Length < 2 || trimmedUser.Length > 64)
                return (false, "用户名长度需在 2 到 64 个字符之间。");

            string normUser = trimmedUser.ToLowerInvariant();
            if (!string.Equals(normUser, user.NormalizedUsername, StringComparison.Ordinal))
            {
                bool taken = await db.PanelUsers
                    .AnyAsync(u => u.Id != userId && u.NormalizedUsername == normUser, cancellationToken)
                    .ConfigureAwait(false);
                if (taken)
                    return (false, $"用户名 '{trimmedUser}' 已被占用。");
            }

            string oldUsername = user.Username;
            user.Username = trimmedUser;
            user.NormalizedUsername = normUser;
            if (!hasNewDisplayName &&
                (string.Equals(user.DisplayName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(user.DisplayName, oldUsername, StringComparison.OrdinalIgnoreCase)))
            {
                user.DisplayName = trimmedUser;
            }

            List<McpTokenEntity> myTokens = await db.McpTokens
                .Where(t => t.OwnerUserId == user.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (McpTokenEntity t in myTokens)
                t.OwnerUsername = trimmedUser;
        }

        if (hasNewDisplayName)
            user.DisplayName = newDisplayName!.Trim();

        if (hasNewPassword)
        {
            (bool valid, string? strengthErr) = Argon2PasswordHasher.ValidatePasswordStrength(newPassword, user.Username);
            if (!valid)
                return (false, strengthErr ?? "新密码不符合要求。");

            user.PasswordHash = _passwordHasher.HashPassword(newPassword!);
        }

        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        int revokedCount = 0;
        if (hasNewPassword)
        {
            // 吊销该账号除当前会话外的所有其他活跃会话，并刷新当前会话缓存
            DateTime now = DateTime.UtcNow;
            List<PanelSessionEntity> otherSessions = await db.PanelSessions
                .Where(s => s.UserId == userId && s.Id != currentSessionId && s.RevokedAt == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (PanelSessionEntity other in otherSessions)
                other.RevokedAt = now;
            revokedCount = otherSessions.Count;
        }

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = user.Id.ToString(),
            ActorName = user.Username,
            Action = "auth.change_password",
            Target = user.Username,
            Ip = ip,
            Result = "ok",
            DetailJson = $"{{\"usernameUpdated\":{(hasNewUsername ? "true" : "false")},\"passwordUpdated\":{(hasNewPassword ? "true" : "false")},\"revokedOtherSessions\":{revokedCount}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, "账号与密码设置已保存。");
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
}
