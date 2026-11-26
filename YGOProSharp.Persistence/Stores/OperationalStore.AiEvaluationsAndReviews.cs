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
    // ==================== 7. 卡组六维量化评分（0-100）、赛后复盘与跨局生涯记忆 ====================

    public static string ComputeDeckOverallTier(
        int burstPower,
        int resilience,
        int stunToxicity,
        int funFactor,
        int complexity,
        int metaAdaptability)
    {
        // 以竞技核心四维为主权重、兼顾控制压制与上限计算综合档位
        double weighted = burstPower * 0.28 +
                          resilience * 0.24 +
                          metaAdaptability * 0.26 +
                          Math.Max(stunToxicity, funFactor) * 0.12 +
                          complexity * 0.10;

        return weighted switch
        {
            >= 86 => "S+",
            >= 76 => "S",
            >= 64 => "A",
            >= 48 => "B",
            _ => "C"
        };
    }

    public static (bool Valid, string? ErrorMessage) ValidateSixDimensionScores(
        int burstPower,
        int resilience,
        int stunToxicity,
        int funFactor,
        int complexity,
        int metaAdaptability)
    {
        (string Name, int Val)[] dims =
        [
            ("burst_power (爆发强度)", burstPower),
            ("resilience (续航韧性)", resilience),
            ("stun_toxicity (阴间指数)", stunToxicity),
            ("fun_factor (娱乐整活度)", funFactor),
            ("complexity (操作复杂度)", complexity),
            ("meta_adaptability (环境适应性)", metaAdaptability)
        ];

        foreach ((string name, int val) in dims)
        {
            if (val is < 0 or > 100)
                return (false, $"维度 {name} 分值必须为 0 ~ 100 之间的整数（当前传入：{val}）。");
        }

        return (true, null);
    }

    public async Task<(bool Success, string Message, DeckEvaluationEntity? Evaluation)> UpsertDeckEvaluationAsync(
        string deckHash,
        string deckName,
        string sourceType,
        string? sourceRef,
        string archetypeTitle,
        int burstPower,
        int resilience,
        int stunToxicity,
        int funFactor,
        int complexity,
        int metaAdaptability,
        string routinesJson,
        string commentary,
        string evaluatorName,
        Guid? evaluatorTokenId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deckHash))
            return (false, "deckHash 不能为空。", null);

        (bool validScores, string? scoreErr) = ValidateSixDimensionScores(
            burstPower, resilience, stunToxicity, funFactor, complexity, metaAdaptability);
        if (!validScores)
            return (false, scoreErr!, null);

        string normHash = deckHash.Trim().ToLowerInvariant();
        string tier = ComputeDeckOverallTier(burstPower, resilience, stunToxicity, funFactor, complexity, metaAdaptability);
        string cleanName = string.IsNullOrWhiteSpace(deckName) ? "UnnamedDeck" : deckName.Trim();
        string cleanArch = string.IsNullOrWhiteSpace(archetypeTitle) ? cleanName : archetypeTitle.Trim();
        string cleanEvalName = string.IsNullOrWhiteSpace(evaluatorName) ? "AI Evaluator" : evaluatorName.Trim();

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        DeckEvaluationEntity? entity = await db.DeckEvaluations
            .FirstOrDefaultAsync(e => e.DeckHash == normHash && e.EvaluatorTokenId == evaluatorTokenId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new DeckEvaluationEntity
            {
                DeckHash = normHash,
                DeckName = cleanName.Length <= 128 ? cleanName : cleanName[..128],
                SourceType = string.IsNullOrWhiteSpace(sourceType) ? "custom_ydk" : sourceType.Trim(),
                SourceRef = sourceRef is { Length: > 128 } ? sourceRef[..128] : sourceRef,
                ArchetypeTitle = cleanArch.Length <= 128 ? cleanArch : cleanArch[..128],
                BurstPower = burstPower,
                Resilience = resilience,
                StunToxicity = stunToxicity,
                FunFactor = funFactor,
                Complexity = complexity,
                MetaAdaptability = metaAdaptability,
                OverallTier = tier,
                RoutinesJson = string.IsNullOrWhiteSpace(routinesJson) ? "{}" : routinesJson,
                Commentary = commentary?.Trim() ?? string.Empty,
                EvaluatorName = cleanEvalName.Length <= 64 ? cleanEvalName : cleanEvalName[..64],
                EvaluatorTokenId = evaluatorTokenId
            };
            db.DeckEvaluations.Add(entity);
        }
        else
        {
            entity.DeckName = cleanName.Length <= 128 ? cleanName : cleanName[..128];
            entity.ArchetypeTitle = cleanArch.Length <= 128 ? cleanArch : cleanArch[..128];
            entity.BurstPower = burstPower;
            entity.Resilience = resilience;
            entity.StunToxicity = stunToxicity;
            entity.FunFactor = funFactor;
            entity.Complexity = complexity;
            entity.MetaAdaptability = metaAdaptability;
            entity.OverallTier = tier;
            entity.RoutinesJson = string.IsNullOrWhiteSpace(routinesJson) ? "{}" : routinesJson;
            entity.Commentary = commentary?.Trim() ?? string.Empty;
            entity.EvaluatorName = cleanEvalName.Length <= 64 ? cleanEvalName : cleanEvalName[..64];
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, $"已保存卡组「{entity.ArchetypeTitle}」六维量化评测（综合评级：{tier}）。", entity);
    }

    public async Task<DeckEvaluationEntity?> GetLatestDeckEvaluationByHashAsync(
        string deckHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deckHash))
            return null;
        string norm = deckHash.Trim().ToLowerInvariant();
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DeckEvaluations
            .AsNoTracking()
            .Where(e => e.DeckHash == norm)
            .OrderByDescending(e => e.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeckEvaluationEntity>> ListDeckEvaluationsAsync(
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 100);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DeckEvaluations
            .AsNoTracking()
            .OrderByDescending(e => e.UpdatedAt)
            .Take(clamped)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(bool Success, string Message, DuelReviewEntity? Review)> SaveDuelReviewAsync(
        long duelRecordId,
        string headline,
        string summary,
        string playerRatingsJson,
        string turningPointsJson,
        string observedPatternsJson,
        string reviewerName,
        string reviewerRole,
        Guid? reviewerTokenId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(summary))
            return (false, "复盘标题 (headline) 与全局总结 (summary) 不能为空。", null);

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        bool exists = await db.DuelRecords.AnyAsync(d => d.Id == duelRecordId, cancellationToken).ConfigureAwait(false);
        if (!exists)
            return (false, $"未找到已结束归档的对局记录 R#{duelRecordId}（进行中对局禁止提交或读取全视角复盘）。", null);

        string cleanHead = headline.Trim();
        string cleanRev = string.IsNullOrWhiteSpace(reviewerName) ? "AI Analyst" : reviewerName.Trim();
        string cleanRole = string.IsNullOrWhiteSpace(reviewerRole) ? "coach" : reviewerRole.Trim();

        DuelReviewEntity entity = new()
        {
            DuelRecordId = duelRecordId,
            Headline = cleanHead.Length <= 200 ? cleanHead : cleanHead[..200],
            Summary = summary.Trim(),
            PlayerRatingsJson = string.IsNullOrWhiteSpace(playerRatingsJson) ? "[]" : playerRatingsJson,
            TurningPointsJson = string.IsNullOrWhiteSpace(turningPointsJson) ? "[]" : turningPointsJson,
            ObservedPatternsJson = string.IsNullOrWhiteSpace(observedPatternsJson) ? "[]" : observedPatternsJson,
            ReviewerName = cleanRev.Length <= 64 ? cleanRev : cleanRev[..64],
            ReviewerRole = cleanRole.Length <= 32 ? cleanRole : cleanRole[..32],
            ReviewerTokenId = reviewerTokenId
        };

        db.DuelReviews.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (true, $"已成功为对局 R#{duelRecordId} 保存赛后复盘与打分报告。", entity);
    }

    public async Task<IReadOnlyList<DuelReviewEntity>> ListDuelReviewsAsync(
        long duelRecordId,
        CancellationToken cancellationToken = default)
    {
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        return await db.DuelReviews
            .AsNoTracking()
            .Where(r => r.DuelRecordId == duelRecordId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AiDuelistMemoryEntity> SaveAiDuelistMemoryAsync(
        Guid tokenId,
        string? deckHash,
        string? opponentArchetype,
        string? opponentName,
        string? result,
        string strategicNote,
        CancellationToken cancellationToken = default)
    {
        string cleanNote = string.IsNullOrWhiteSpace(strategicNote) ? string.Empty : strategicNote.Trim();
        if (cleanNote.Length == 0)
            throw new ArgumentException("strategicNote 不能为空。", nameof(strategicNote));
        if (cleanNote.Length > 550)
            cleanNote = cleanNote[..550];

        string normRes = (result?.Trim().ToLowerInvariant()) switch
        {
            "win" or "victory" => "win",
            "loss" or "defeat" or "lose" => "loss",
            "draw" or "tie" => "draw",
            _ => "note"
        };

        await using OperationalDbContext db = _dbManager.CreateDbContext();
        AiDuelistMemoryEntity created = new()
        {
            TokenId = tokenId,
            DeckHash = string.IsNullOrWhiteSpace(deckHash) ? null : deckHash.Trim().ToLowerInvariant(),
            OpponentArchetype = string.IsNullOrWhiteSpace(opponentArchetype) ? null : (opponentArchetype.Trim().Length <= 128 ? opponentArchetype.Trim() : opponentArchetype.Trim()[..128]),
            OpponentName = string.IsNullOrWhiteSpace(opponentName) ? null : (opponentName.Trim().Length <= 64 ? opponentName.Trim() : opponentName.Trim()[..64]),
            Result = normRes,
            StrategicNote = cleanNote
        };
        db.AiDuelistMemories.Add(created);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 有界裁剪：单个 Token 最多保留最新 50 条高价值实战记忆
        List<AiDuelistMemoryEntity> overflow = await db.AiDuelistMemories
            .Where(m => m.TokenId == tokenId)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (overflow.Count > 0)
        {
            DateTime now = DateTime.UtcNow;
            foreach (AiDuelistMemoryEntity old in overflow)
                old.DeletedAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    public async Task<IReadOnlyList<AiDuelistMemoryEntity>> QueryAiDuelistMemoriesAsync(
        Guid tokenId,
        string? deckHash = null,
        string? opponentKeyword = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        int clamped = Math.Clamp(limit, 1, 50);
        await using OperationalDbContext db = _dbManager.CreateDbContext();
        IQueryable<AiDuelistMemoryEntity> q = db.AiDuelistMemories
            .AsNoTracking()
            .Where(m => m.TokenId == tokenId);

        if (!string.IsNullOrWhiteSpace(deckHash))
        {
            string normHash = deckHash.Trim().ToLowerInvariant();
            q = q.Where(m => m.DeckHash == normHash);
        }

        if (!string.IsNullOrWhiteSpace(opponentKeyword))
        {
            string kw = opponentKeyword.Trim();
            q = q.Where(m =>
                (m.OpponentArchetype != null && m.OpponentArchetype.Contains(kw)) ||
                (m.OpponentName != null && m.OpponentName.Contains(kw)) ||
                m.StrategicNote.Contains(kw));
        }

        return await q.OrderByDescending(m => m.CreatedAt).Take(clamped).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
