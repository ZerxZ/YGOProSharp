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
    // ==================== 6. DIY 提案门禁（Proposal Gate） ====================

    public async Task<IReadOnlyList<DiyProposalEntity>> ListDiyProposalsAsync(
        string? statusFilter = null,
        string? packageId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 300);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<DiyProposalEntity> q = db.DiyProposals.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            string normStatus = statusFilter.Trim().ToLowerInvariant();
            q = q.Where(p => p.Status == normStatus);
        }

        if (!string.IsNullOrWhiteSpace(packageId))
        {
            string normPkg = packageId.Trim();
            q = q.Where(p => p.PackageId == normPkg);
        }

        return await q.OrderByDescending(p => p.CreatedAt).Take(clamped).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiyProposalEntity?> GetDiyProposalByIdAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DiyProposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiyProposalEntity> CreateDiyProposalAsync(
        string packageId,
        string title,
        string diffJson,
        Guid? proposerTokenId,
        string proposerName,
        string actorType = "panel_user",
        string? actorId = null,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("PackageId cannot be empty.", nameof(packageId));

        DiyProposalEntity entity = new()
        {
            PackageId = packageId.Trim(),
            ProposerTokenId = proposerTokenId,
            Title = string.IsNullOrWhiteSpace(title) ? $"DIY 提案 ({packageId.Trim()})" : title.Trim(),
            DiffJson = string.IsNullOrWhiteSpace(diffJson) ? "{}" : diffJson,
            Status = "pending"
        };

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        db.DiyProposals.Add(entity);
        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = actorType,
            ActorId = actorId,
            ActorName = string.IsNullOrWhiteSpace(proposerName) ? "unknown" : proposerName.Trim(),
            Action = "diy.proposal_create",
            Target = $"{entity.PackageId}:{entity.Title}",
            Ip = ip,
            Result = "ok"
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<(bool Success, string Message, DiyProposalEntity? Proposal)> ReviewDiyProposalAsync(
        AuthenticatedPrincipal reviewer,
        Guid proposalId,
        bool approve,
        string? reviewComment,
        Func<DiyProposalEntity, CancellationToken, Task<(bool Applied, string? ErrorMessage)>>? applyApprovedChangesAsync,
        string? ip,
        CancellationToken cancellationToken = default)
    {
        if (!reviewer.HasPermission(RbacPermissions.DiyReviewProposal))
            return (false, "缺少 diy:review_proposal 审批权限。", null);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        DiyProposalEntity? proposal = await db.DiyProposals.FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken).ConfigureAwait(false);
        if (proposal is null)
            return (false, "提案不存在。", null);

        if (!string.Equals(proposal.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return (false, $"该提案当前已处于 '{proposal.Status}' 状态，无法重复审批。", proposal);

        if (approve && applyApprovedChangesAsync is not null)
        {
            (bool applied, string? applyErr) = await applyApprovedChangesAsync(proposal, cancellationToken).ConfigureAwait(false);
            if (!applied)
            {
                await AppendAuditAsync("panel_user", reviewer.UserId.ToString(), reviewer.Username, "diy.proposal_approve", RbacPermissions.DiyReviewProposal, proposal.Id.ToString(), ip, "error", applyErr, cancellationToken).ConfigureAwait(false);
                return (false, $"应用提案变更失败：{applyErr}", proposal);
            }
        }

        proposal.Status = approve ? "approved" : "rejected";
        proposal.ReviewerUserId = reviewer.UserId;
        proposal.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();
        proposal.ReviewedAt = DateTime.UtcNow;

        db.AuditLogs.Add(new AuditLogEntity
        {
            ActorType = "panel_user",
            ActorId = reviewer.UserId.ToString(),
            ActorName = reviewer.Username,
            Action = approve ? "diy.proposal_approve" : "diy.proposal_reject",
            PermissionChecked = RbacPermissions.DiyReviewProposal,
            Target = $"{proposal.PackageId}:{proposal.Title}",
            Ip = ip,
            Result = "ok",
            DetailJson = proposal.ReviewComment is null ? null : JsonSerializer.Serialize(new { comment = proposal.ReviewComment })
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, approve ? "提案已批准并原子生效。" : "提案已驳回。", proposal);
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
}
