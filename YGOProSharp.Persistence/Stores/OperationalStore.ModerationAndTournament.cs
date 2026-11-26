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

    public async Task<TournamentDeckEntity> SubmitPlayerTournamentDeckAsync(
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
            ActorType = "anonymous",
            ActorName = playerName.Trim(),
            Action = "tournament_deck.player_submit",
            Target = $"{tag}:{playerName.Trim()}",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }
}
