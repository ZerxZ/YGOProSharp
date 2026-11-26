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

        await using OperationalDbContext db = _dbManager.CreateDbContext();

        string trimmedUsername;
        if (string.IsNullOrWhiteSpace(username))
        {
            // 未填写用户名时自动生成默认账号名
            bool isAdminRole = roleCodes.Any(static r => string.Equals(r, BuiltInRoleCodes.SuperAdmin, StringComparison.OrdinalIgnoreCase));
            string basePrefix = isAdminRole ? "admin" : (roleCodes.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "user");
            if (isAdminRole && !await db.PanelUsers.AnyAsync(u => u.NormalizedUsername == "admin", cancellationToken).ConfigureAwait(false))
            {
                trimmedUsername = "admin";
            }
            else
            {
                trimmedUsername = $"{basePrefix}_{TokenCrypto.GenerateShortCode(4).ToLowerInvariant()}";
            }
        }
        else
        {
            trimmedUsername = username.Trim();
            if (trimmedUsername.Length < 2 || trimmedUsername.Length > 64)
                return (false, "用户名长度需在 2 到 64 个字符之间。", null, null);
        }

        string normalizedUsername = trimmedUsername.ToLowerInvariant();
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
        CancellationToken cancellationToken = default,
        string? newUsername = null)
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

        if (!string.IsNullOrWhiteSpace(newUsername))
        {
            string trimmedNewUser = newUsername.Trim();
            if (trimmedNewUser.Length < 2 || trimmedNewUser.Length > 64)
                return (false, "用户名长度需在 2 到 64 个字符之间。");

            string normNewUser = trimmedNewUser.ToLowerInvariant();
            if (!string.Equals(normNewUser, user.NormalizedUsername, StringComparison.Ordinal))
            {
                bool exists = await db.PanelUsers
                    .AnyAsync(u => u.Id != targetUserId && u.NormalizedUsername == normNewUser, cancellationToken)
                    .ConfigureAwait(false);
                if (exists)
                    return (false, $"用户名 '{trimmedNewUser}' 已被占用。");
            }

            string prevName = user.Username;
            user.Username = trimmedNewUser;
            user.NormalizedUsername = normNewUser;
            if (string.IsNullOrWhiteSpace(displayName) &&
                (string.Equals(user.DisplayName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(user.DisplayName, prevName, StringComparison.OrdinalIgnoreCase)))
            {
                user.DisplayName = trimmedNewUser;
            }

            List<McpTokenEntity> userTokens = await db.McpTokens
                .Where(t => t.OwnerUserId == user.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (McpTokenEntity t in userTokens)
                t.OwnerUsername = trimmedNewUser;
        }

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
        CancellationToken cancellationToken = default,
        string? customPassword = null,
        bool? mustChangePassword = null)
    {
        if (!actor.HasPermission(RbacPermissions.UserManage))
            return (false, "缺少 user:manage 权限。", null);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        PanelUserEntity? user = await db.PanelUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
            return (false, "目标用户不存在。", null);

        // 权限不放大保护：低权限管理员不可重置更高权限账号（如 super_admin）的密码
        HashSet<string> targetEffective = RbacPolicyEvaluator.ComputeEffectivePermissions(
            user.UserRoles.Where(static ur => ur.Role is not null).Select(static ur => RbacPolicyEvaluator.ParsePermissionsJson(ur.Role.PermissionsJson)),
            RbacPolicyEvaluator.ParseOverrideJson(user.PermissionsOverrideJson));
        if (!RbacPolicyEvaluator.IsSubsetOfActorPermissions(actor.EffectivePermissions, targetEffective))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "user.reset_password", RbacPermissions.UserManage, user.Username, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, "权限不放大保护：无法重置拥有高于自身权限的账号密码。", null);
        }

        string? generatedTempPassword = null;
        string rawPasswordToSet;
        bool forceChange;

        if (string.IsNullOrWhiteSpace(customPassword))
        {
            generatedTempPassword = TokenCrypto.GenerateBootstrapPassword(16);
            rawPasswordToSet = generatedTempPassword;
            forceChange = mustChangePassword ?? true;
        }
        else
        {
            (bool valid, string? pwErr) = Argon2PasswordHasher.ValidatePasswordStrength(customPassword, user.Username);
            if (!valid)
                return (false, pwErr ?? "自定义密码不符合要求。", null);

            rawPasswordToSet = customPassword;
            forceChange = mustChangePassword ?? false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(rawPasswordToSet);
        user.MustChangePassword = forceChange;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        DateTime now = DateTime.UtcNow;
        List<PanelSessionEntity> sessions = await db.PanelSessions
            .Where(s => s.UserId == user.Id && s.Id != actor.SessionId && s.RevokedAt == null)
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
            DetailJson = $"{{\"customPassword\":{(generatedTempPassword is null ? "true" : "false")},\"revokedSessions\":{sessions.Count}}}"
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);

        string msg = generatedTempPassword is null
            ? $"已将用户 '{user.Username}' 的密码更新为自定义密码。"
            : "已重置为默认随机密码并要求下次登录确认。";
        return (true, msg, generatedTempPassword);
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
}
