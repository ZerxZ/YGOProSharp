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

    public static bool IsMechanicalAiName(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return true;

        string t = candidate.Trim();
        if (t.StartsWith("待AI", StringComparison.OrdinalIgnoreCase))
            return true;

        // 拦截如 AI, AI1, AI2, ai_01, Bot, Bot2, Player1, Seat1, Room01-AI1, Room-Seat2 等生硬编号名
        return System.Text.RegularExpressions.Regex.IsMatch(
            t,
            @"^(?:ai|bot|player|duelist|seat|user|mcp|agent|robot)[-_#\s]*\d*$|[-_](?:ai|seat|bot)\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static (bool Valid, string? CleanNickname, string? ErrorMessage) ValidateAndCleanAiNickname(string? rawNickname, bool rejectMechanical = true)
    {
        if (string.IsNullOrWhiteSpace(rawNickname))
            return (false, null, "昵称不能为空。");

        string cleaned = new string(rawNickname.Trim().Where(static c => !char.IsControl(c) && c is not ('#' or '$' or ';' or '\r' or '\n')).ToArray()).Trim();
        if (cleaned.Length < 2 || cleaned.Length > 18)
            return (false, null, "决斗者昵称长度需在 2 ~ 18 个字符之间（不含 # $ 符号）。");

        if (rejectMechanical && IsMechanicalAiName(cleaned))
            return (false, null, $"请不要使用 '{cleaned}' 这类机械编号名称，请取一个有个性的真实风格昵称（例如『星尘观测者』『深渊魔导』『青眼龙骑』）。");

        return (true, cleaned, null);
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
        CancellationToken cancellationToken = default,
        string? preferredPlayerName = null)
    {
        string normalizedRole = string.IsNullOrWhiteSpace(mcpRole) ? McpRoleCodes.Player : mcpRole.Trim().ToLowerInvariant();
        if (!McpRoleCodes.All.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase))
            return (false, $"不支持的 MCP 角色 '{mcpRole}'。", null);

        if (!RbacPolicyEvaluator.CanIssueMcpRole(actor.RoleCodes, actor.EffectivePermissions, normalizedRole))
        {
            await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "mcp_token.issue", RbacPermissions.McpTokenIssueSelf, normalizedRole, ip, "privilege_escalation_blocked", null, cancellationToken).ConfigureAwait(false);
            return (false, $"权限不放大保护：当前账号无权签发 '{normalizedRole}' 级别的 MCP Token。", null);
        }

        string? resolvedPreferredName = null;
        if (!string.IsNullOrWhiteSpace(preferredPlayerName))
        {
            (bool nickOk, string? cleanNick, string? nickErr) = ValidateAndCleanAiNickname(preferredPlayerName, rejectMechanical: true);
            if (!nickOk)
                return (false, nickErr ?? "AI 昵称格式不合法。", null);
            resolvedPreferredName = cleanNick;
        }
        else if (!string.IsNullOrWhiteSpace(name) && !IsMechanicalAiName(name))
        {
            (bool nickOk, string? cleanNick, _) = ValidateAndCleanAiNickname(name, rejectMechanical: true);
            if (nickOk)
                resolvedPreferredName = cleanNick;
        }

        string displayLabel = !string.IsNullOrWhiteSpace(resolvedPreferredName)
            ? resolvedPreferredName
            : (!string.IsNullOrWhiteSpace(name) ? name.Trim() : "待AI首次自取名");

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
            Name = displayLabel,
            PreferredPlayerName = resolvedPreferredName,
            TokenPrefix = prefix,
            TokenSha256 = sha256,
            McpRole = normalizedRole,
            AllowedRoomsJson = cleanRooms.Length == 0 ? null : JsonSerializer.Serialize(cleanRooms),
            MaxSessions = Math.Clamp(maxSessions, 1, 16),
            RequireApproval = requireApproval,
            ExpiresAt = expiresInHours is > 0 ? DateTime.UtcNow.AddHours(expiresInHours.Value) : null
        };

        await using OperationalDbContext db = _dbManager.CreateDbContext();

        if (!actor.HasPermission(RbacPermissions.McpTokenManageAll) && !actor.HasPermission(RbacPermissions.UserManage))
        {
            DateTime now = DateTime.UtcNow;
            int activeCount = await db.McpTokens
                .CountAsync(t => t.OwnerUserId == actor.UserId && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now), cancellationToken)
                .ConfigureAwait(false);
            if (activeCount >= 20)
                return (false, "个人活跃 AI Token 数量已达上限（最多 20 个），请先吊销不再使用的旧 Token。", null);
        }

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
                    token.PreferredPlayerName,
                    token.PersonaStyle,
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
            cached.RequireApproval,
            cached.PreferredPlayerName,
            cached.PersonaStyle);
    }

    public async Task<(bool Success, string Message, string? BoundName, string? BoundPersona)> BindMcpTokenProfileAsync(
        Guid tokenId,
        string? chosenNickname,
        string? personaStyle,
        CancellationToken cancellationToken = default)
    {
        string? cleanNick = null;
        if (!string.IsNullOrWhiteSpace(chosenNickname))
        {
            (bool valid, string? c, string? err) = ValidateAndCleanAiNickname(chosenNickname, rejectMechanical: true);
            if (!valid || c is null)
                return (false, err ?? "AI 昵称无效。", null, null);
            cleanNick = c;
        }

        string? cleanPersona = null;
        if (personaStyle is not null)
        {
            string trimmedPersona = new string(personaStyle.Trim().Where(static ch => !char.IsControl(ch)).ToArray()).Trim();
            cleanPersona = trimmedPersona.Length == 0
                ? null
                : (trimmedPersona.Length <= 100 ? trimmedPersona : trimmedPersona[..100]);
        }

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        McpTokenEntity? token = await db.McpTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
            return (false, "Token 不存在或已吊销。", null, null);

        if (cleanNick is not null)
        {
            token.PreferredPlayerName = cleanNick;
            if (IsMechanicalAiName(token.Name))
                token.Name = cleanNick;
        }

        if (personaStyle is not null)
            token.PersonaStyle = cleanPersona;

        token.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        return (true, $"已更新决斗者档案（昵称：{token.PreferredPlayerName ?? "<未设定>"}，人设：{token.PersonaStyle ?? "默认"}）。", token.PreferredPlayerName, token.PersonaStyle);
    }

    public async Task<(bool Success, string Message, string? BoundName)> BindMcpTokenPlayerNameAsync(
        Guid tokenId,
        string chosenNickname,
        CancellationToken cancellationToken = default)
    {
        (bool ok, string msg, string? bound, _) = await BindMcpTokenProfileAsync(tokenId, chosenNickname, personaStyle: null, cancellationToken).ConfigureAwait(false);
        return (ok, msg, bound);
    }

    public async Task<(bool Success, string Message, string? PreferredPlayerName)> UpdateMcpTokenNicknameAsync(
        AuthenticatedPrincipal actor,
        Guid tokenId,
        string? newPreferredNickname,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        string? cleaned = null;
        if (!string.IsNullOrWhiteSpace(newPreferredNickname))
        {
            (bool valid, string? c, string? err) = ValidateAndCleanAiNickname(newPreferredNickname, rejectMechanical: true);
            if (!valid)
                return (false, err ?? "昵称不合法。", null);
            cleaned = c;
        }

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        McpTokenEntity? token = await db.McpTokens
            .FirstOrDefaultAsync(t => t.Id == tokenId && t.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
            return (false, "未找到该 AI Token。", null);

        bool canManageAll = actor.HasPermission(RbacPermissions.McpTokenManageAll) || actor.HasPermission(RbacPermissions.UserManage);
        if (token.OwnerUserId != actor.UserId && !canManageAll)
            return (false, "无权修改他人的 AI Token。", null);

        token.PreferredPlayerName = cleaned;
        token.Name = cleaned ?? "待AI首次自取名";
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.FlushNamespaceAsync("auth", cancellationToken).ConfigureAwait(false);
        await AppendAuditAsync("panel_user", actor.UserId.ToString(), actor.Username, "mcp_token.rename", RbacPermissions.McpTokenIssueSelf, $"{tokenId}:{cleaned ?? "<auto>"}", ip, "ok", null, cancellationToken).ConfigureAwait(false);

        return (true, cleaned is null ? "已重置为由 AI 下次进房时自主取名。" : $"已将 AI 决斗昵称设为「{cleaned}」。", cleaned);
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

    private sealed record CachedMcpTokenDto(
        Guid TokenId,
        Guid OwnerUserId,
        string OwnerUsername,
        string TokenSha256,
        string Name,
        string? PreferredPlayerName,
        string? PersonaStyle,
        string McpRole,
        string[] AllowedRooms,
        int MaxSessions,
        bool RequireApproval,
        DateTime? ExpiresAt);
}
