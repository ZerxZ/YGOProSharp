using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using YGOProSharp.Core;
using YGOProSharp.Mcp.Services;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Mcp.Tools;

public sealed record DuelReviewPlayerRatingInput(
    string? PlayerName = null,
    int? PlayScore = null,
    int? Score = null,
    int? SeatIndex = null,
    int? TeamworkScore = null,
    [property: JsonPropertyName("teamwork_score")] int? TeamworkScoreAlt = null,
    bool? Mvp = null,
    string[]? Highlights = null,
    string[]? Mistakes = null,
    string[]? BetterLines = null);

public sealed record DuelReviewTurningPointInput(
    int? Turn = null,
    string? Phase = null,
    string? Actor = null,
    string? Impact = null,
    string? Analysis = null);

public sealed record DuelReviewPatternInput(
    string? PlayerName = null,
    string? PatternName = null,
    string[]? CardsInvolved = null,
    string? Effectiveness = null);

/// <summary>
/// 卡组静态构筑特征剖析、0~100 六维量化评分提交/查询与赛后已完结对局（R#&lt;id&gt;）全视角复盘工具。
/// </summary>
public sealed class DeckAndReplayReviewTools(McpRuntimeDependencies deps, DeckAndReplayAppService appService)
{
    private static readonly JsonSerializerOptions CompactCamelJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [McpServerTool(Name = "deck_analyze_features")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("对预设卡组、.ydk 文本或已结束归档对局（R#<id>）中的某席位卡组执行确定性静态特征剖析（计算无视顺序的标准 DeckHash、怪魔陷配比曲线、本家字段集中度、客观效果词频与全部卡片效文），并附带已有的六维评分。")]
    public async Task<ToolOutcome> DeckAnalyzeFeaturesAsync(
        [Description("预设卡组名称或 ID")] string? presetName = null,
        [Description("原始 .ydk 卡组文本")] string? ydkContent = null,
        [Description("自定义卡组标题")] string? deckName = null,
        [Description("可选：从已结束对局（如 R#1 或 1）提取参赛选手卡组")] JsonElement? duelId = null,
        [Description("配合 duelId 指定选手席位下标 (0~3，默认 0)")] int seatIndex = 0,
        CancellationToken ct = default)
    {
        long? parsedDuelId = null;
        if (duelId is not null && duelId.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (!McpArgHelpers.TryParseDuelId(duelId, out long did))
                return ToolOutcome.Fail(new { success = false, message = "duelId 格式无效（请传入如 1 或 'R#1'）。" });
            parsedDuelId = did;
        }

        (bool resolved, string err, YdkDeckData? deckData, string sourceType, string? sourceRef) =
            await appService.ResolveTargetDeckAsync(presetName, ydkContent, deckName, parsedDuelId, seatIndex, ct).ConfigureAwait(false);
        if (!resolved || deckData is null)
            return ToolOutcome.Fail(new { success = false, message = err });

        (DeckStaticProfile profile, object? existingEvaluation) = await appService.AnalyzeDeckAsync(deckData, ct).ConfigureAwait(false);
        return ToolOutcome.Ok(new
        {
            success = true,
            sourceType,
            sourceRef,
            profile,
            existingEvaluation
        });
    }

    [McpServerTool(Name = "deck_submit_evaluation")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("提交外部 AI 对某套卡组的六维量化评分（每项严格要求 0~100 整数：爆发强度 burst_power、续航韧性 resilience、阴间指数 stun_toxicity、娱乐整活度 fun_factor、操作复杂度 complexity、环境适应性 meta_adaptability）及核心展开套路。")]
    public async Task<ToolOutcome> DeckSubmitEvaluationAsync(
        [Description("爆发强度 (0~100 整数)")] int? burst_power = null,
        [Description("续航韧性 (0~100 整数)")] int? resilience = null,
        [Description("阴间指数/封锁折磨度 (0~100 整数)")] int? stun_toxicity = null,
        [Description("娱乐整活度/魂度 (0~100 整数)")] int? fun_factor = null,
        [Description("操作复杂度 (0~100 整数)")] int? complexity = null,
        [Description("环境适应性/泛用度 (0~100 整数)")] int? meta_adaptability = null,
        [Description("deck_analyze_features 返回的 64 位标准 deckHash（若传 presetName 或 ydkContent 可自动计算）")] string? deckHash = null,
        string? presetName = null,
        string? ydkContent = null,
        string? deckName = null,
        [Description("识别出的卡组流派标题")] string? archetypeTitle = null,
        [Description("核心展开/配合套路列表")] string[]? coreRoutines = null,
        [Description("起手初动点列表 (Starters)")] string[]? starters = null,
        [Description("标准目标终场阻抗 (Target Endboard)")] string[]? targetEndboard = null,
        [Description("长盘续航与资源回收轴 (Grind Loop)")] string[]? grindLoop = null,
        [Description("怕吃手坑/阻抗的关键弱点 (Chokepoints)")] string[]? chokepoints = null,
        [Description("构筑优势")] string[]? strengths = null,
        [Description("构筑短板与怕吃的针对卡")] string[]? weaknesses = null,
        [Description("改构建议")] string[]? upgradeSuggestions = null,
        [Description("综合评语")] string? commentary = null,
        int? burstPower = null,
        int? stunToxicity = null,
        int? funFactor = null,
        int? metaAdaptability = null,
        CancellationToken ct = default)
    {
        McpCallerScope scope = McpCallerScope.Current;
        ValidatedMcpToken caller = scope.Caller;
        string effHash = deckHash?.Trim() ?? string.Empty;
        string effDeckName = deckName?.Trim() ?? string.Empty;
        string sourceType = "hash";
        string? sourceRef = null;

        if (string.IsNullOrWhiteSpace(effHash) || !string.IsNullOrWhiteSpace(presetName) || !string.IsNullOrWhiteSpace(ydkContent))
        {
            (bool resolved, string err, YdkDeckData? data, string sType, string? sRef) =
                await appService.ResolveTargetDeckAsync(presetName, ydkContent, effDeckName, null, 0, ct).ConfigureAwait(false);
            if (!resolved || data is null)
            {
                if (string.IsNullOrWhiteSpace(effHash))
                    return ToolOutcome.Fail(new { success = false, message = err });
            }
            else
            {
                effHash = DeckStaticProfileBuilder.ComputeCanonicalDeckHash(data);
                if (string.IsNullOrWhiteSpace(effDeckName))
                    effDeckName = data.Name;
                sourceType = sType;
                sourceRef = sRef;
            }
        }

        if (!McpArgHelpers.ValidateScoreRange(burst_power ?? burstPower, "burst_power", out int burst, out string? sErr) ||
            !McpArgHelpers.ValidateScoreRange(resilience, "resilience", out int resScore, out sErr) ||
            !McpArgHelpers.ValidateScoreRange(stun_toxicity ?? stunToxicity, "stun_toxicity", out int stun, out sErr) ||
            !McpArgHelpers.ValidateScoreRange(fun_factor ?? funFactor, "fun_factor", out int fun, out sErr) ||
            !McpArgHelpers.ValidateScoreRange(complexity, "complexity", out int comp, out sErr) ||
            !McpArgHelpers.ValidateScoreRange(meta_adaptability ?? metaAdaptability, "meta_adaptability", out int meta, out sErr))
        {
            return ToolOutcome.Fail(new { success = false, error = "invalid_dimension_score", message = sErr });
        }

        string routinesJson = JsonSerializer.Serialize(new
        {
            coreRoutines = CleanStringArray(coreRoutines),
            starters = CleanStringArray(starters),
            targetEndboard = CleanStringArray(targetEndboard),
            grindLoop = CleanStringArray(grindLoop),
            chokepoints = CleanStringArray(chokepoints),
            strengths = CleanStringArray(strengths),
            weaknesses = CleanStringArray(weaknesses),
            upgradeSuggestions = CleanStringArray(upgradeSuggestions)
        }, CompactCamelJson);

        string evaluatorName = caller.PreferredPlayerName ?? caller.Name;
        (bool ok, string msg, DeckEvaluationEntity? saved) = await deps.Store.UpsertDeckEvaluationAsync(
            deckHash: effHash,
            deckName: string.IsNullOrWhiteSpace(effDeckName) ? "EvaluatedDeck" : effDeckName,
            sourceType: sourceType,
            sourceRef: sourceRef,
            archetypeTitle: archetypeTitle?.Trim() ?? string.Empty,
            burstPower: burst,
            resilience: resScore,
            stunToxicity: stun,
            funFactor: fun,
            complexity: comp,
            metaAdaptability: meta,
            routinesJson: routinesJson,
            commentary: commentary?.Trim() ?? string.Empty,
            evaluatorName: evaluatorName,
            evaluatorTokenId: caller.TokenId,
            cancellationToken: ct).ConfigureAwait(false);

        if (!ok || saved is null)
            return ToolOutcome.Fail(new { success = false, error = "invalid_evaluation", message = msg });

        await deps.Store.AppendAuditAsync("mcp_token", caller.TokenId.ToString(), evaluatorName, "mcp.deck_submit_evaluation", caller.McpRole, saved.DeckHash, scope.RemoteIp, "ok", saved.OverallTier, ct).ConfigureAwait(false);
        deps.PublishPublicEventCallback?.Invoke("deck.evaluated", new { saved.DeckHash, saved.ArchetypeTitle, saved.OverallTier, saved.EvaluatorName });

        return ToolOutcome.Ok(new
        {
            success = true,
            message = msg,
            evaluation = DeckAndReplayAppService.FormatDeckEvaluationDto(saved)
        });
    }

    [McpServerTool(Name = "deck_get_evaluation")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("按 deckHash、presetName 或 ydkContent 查询已保存的卡组六维量化评测结果；若均不传则列出全服最新评测榜单。")]
    public async Task<ToolOutcome> DeckGetEvaluationAsync(
        string? deckHash = null,
        string? presetName = null,
        string? ydkContent = null,
        [Description("列表模式返回条数 (1~50，默认 15)")] int limit = 15,
        CancellationToken ct = default)
    {
        string effHash = deckHash?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(effHash) && (!string.IsNullOrWhiteSpace(presetName) || !string.IsNullOrWhiteSpace(ydkContent)))
        {
            (bool resolved, string err, YdkDeckData? data, _, _) =
                await appService.ResolveTargetDeckAsync(presetName, ydkContent, null, null, 0, ct).ConfigureAwait(false);
            if (!resolved || data is null)
                return ToolOutcome.Fail(new { success = false, message = err });
            effHash = DeckStaticProfileBuilder.ComputeCanonicalDeckHash(data);
        }

        if (!string.IsNullOrWhiteSpace(effHash))
        {
            DeckEvaluationEntity? found = await deps.Store.GetLatestDeckEvaluationByHashAsync(effHash, ct).ConfigureAwait(false);
            if (found is null)
                return ToolOutcome.Ok(new { found = false, deckHash = effHash.ToLowerInvariant(), evaluation = (object?)null });
            return ToolOutcome.Ok(new { found = true, deckHash = found.DeckHash, evaluation = DeckAndReplayAppService.FormatDeckEvaluationDto(found) });
        }

        int clamped = Math.Clamp(limit, 1, 50);
        IReadOnlyList<DeckEvaluationEntity> list = await deps.Store.ListDeckEvaluationsAsync(clamped, ct).ConfigureAwait(false);
        return ToolOutcome.Ok(new
        {
            count = list.Count,
            evaluations = list.Select(DeckAndReplayAppService.FormatDeckEvaluationDto).ToArray()
        });
    }

    [McpServerTool(Name = "duel_list_replays")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("列出服务器上已完结并归档的历史对局列表（R#<id>），供赛后教练 AI 挑选未复盘比赛调用 duel_get_replay_timeline 与 duel_submit_review（严格仅含已结束对局且零 IP 信息）。")]
    public async Task<ToolOutcome> DuelListReplaysAsync(
        [Description("返回条数 (1~50，默认 15)")] int limit = 15,
        [Description("可选：按选手昵称过滤")] string? player = null,
        CancellationToken ct = default)
    {
        int clamped = Math.Clamp(limit, 1, 50);
        IReadOnlyList<DuelRecordEntity> list = await deps.Store.ListDuelRecordsAsync(
            clamped,
            beforeId: null,
            playerFilter: player,
            cancellationToken: ct).ConfigureAwait(false);

        List<object> items = new(list.Count);
        foreach (DuelRecordEntity r in list)
        {
            IReadOnlyList<DuelReviewEntity> revs = await deps.Store.ListDuelReviewsAsync(r.Id, ct).ConfigureAwait(false);
            items.Add(new
            {
                duelId = r.Id,
                replayCode = $"R#{r.Id}",
                r.RoomName,
                r.Mode,
                isTag2v2 = r.Mode == 2,
                r.RandomDeckMode,
                r.TurnCount,
                r.WinnerTeam,
                r.EndedAt,
                reviewCount = revs.Count,
                players = r.Players.OrderBy(static p => p.SeatIndex).Select(static p => new
                {
                    p.SeatIndex,
                    p.TeamId,
                    p.PlayerName,
                    p.SeatType,
                    p.IsWinner
                }).ToArray()
            });
        }

        return ToolOutcome.Ok(new
        {
            count = items.Count,
            replays = items
        });
    }

    [McpServerTool(Name = "duel_get_replay_timeline")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("读取已结束并归档的历史对局（R#<id>）全视角复盘数据：含双方/四方完整起始卡组名称清单、标准 deckHash、胜负结果与逐回合公开事件/AI思考时间轴（进行中的活动房间严禁调用，确保 100% 零透视作弊）。")]
    public async Task<ToolOutcome> DuelGetReplayTimelineAsync(
        [Description("已结束对局编号，支持传整数 12 或回放码 'R#12'")] JsonElement? duelId = null,
        CancellationToken ct = default)
    {
        if (!McpArgHelpers.TryParseDuelId(duelId, out long parsedId))
        {
            return ToolOutcome.Fail(new
            {
                success = false,
                error = "invalid_duel_id",
                message = "请传入有效的已结束对局编号（如 1 或 'R#1'）。进行中的活动房间名称不可用于全视角复盘。"
            });
        }

        object? detail = await appService.GetArchivedReplayDetailAsync(parsedId, includeFullExtraAndAllMain: true, ct).ConfigureAwait(false);
        if (detail is null)
        {
            return ToolOutcome.Fail(new
            {
                success = false,
                error = "duel_not_finished_or_not_found",
                message = $"未找到已完结归档的对局记录 R#{parsedId}。出于战争迷雾反作弊原则，进行中对局严禁读取全视角卡组与复盘数据！"
            });
        }

        return ToolOutcome.Ok(detail);
    }

    [McpServerTool(Name = "duel_submit_review")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("对已结束归档的对局（R#<id>）提交结构化 AI 复盘与选手表现评分报告（playScore 为 0~100 整数；2v2 双打可附加 0~100 的 teamworkScore）；可选通过 lessonForMemory 同步写入自身跨局生涯记忆。")]
    public async Task<ToolOutcome> DuelSubmitReviewAsync(
        [Description("已结束对局编号，如 1 或 'R#1'")] JsonElement? duelId,
        [Description("一句话复盘标题")] string headline,
        [Description("全局战局走向与胜负原因详述")] string summary,
        [Description("各席位选手表现打分（0~100 整数）")] DuelReviewPlayerRatingInput[] playerRatings,
        [Description("关键胜负手转折点列表")] DuelReviewTurningPointInput[]? turningPoints = null,
        [Description("本局实际打出的套路配合或败因模式")] DuelReviewPatternInput[]? observedPatterns = null,
        [Description("self_player|spectator_caster|coach（默认 coach）")] string? reviewerRole = null,
        [Description("可选：将本局提炼的一条实战教训顺带存入当前 Token 的跨局生涯记忆库")] string? lessonForMemory = null,
        string? deckHash = null,
        string? opponentArchetype = null,
        string? opponentName = null,
        string? result = null,
        CancellationToken ct = default)
    {
        if (!McpArgHelpers.TryParseDuelId(duelId, out long parsedDuelId))
            return ToolOutcome.Fail(new { success = false, message = "duelId 格式无效（请传入如 1 或 'R#1'）。" });

        if (playerRatings is null || playerRatings.Length == 0)
            return ToolOutcome.Fail(new { success = false, message = "playerRatings 数组不能为空，请至少为参赛选手给出 0~100 整数评分。" });

        List<object> normalizedRatings = new(playerRatings.Length);
        foreach (DuelReviewPlayerRatingInput r in playerRatings)
        {
            if (!McpArgHelpers.ValidateScoreRange(r.PlayScore ?? r.Score, "playScore", out int playScore, out string? scoreErr))
                return ToolOutcome.Fail(new { success = false, error = "invalid_play_score", message = scoreErr });

            int? rawTeamwork = r.TeamworkScore ?? r.TeamworkScoreAlt;
            int? validTeamwork = null;
            if (rawTeamwork is not null)
            {
                if (!McpArgHelpers.ValidateScoreRange(rawTeamwork, "teamworkScore", out int tw, out string? twErr))
                    return ToolOutcome.Fail(new { success = false, error = "invalid_teamwork_score", message = twErr });
                validTeamwork = tw;
            }

            normalizedRatings.Add(new
            {
                r.SeatIndex,
                playerName = r.PlayerName ?? string.Empty,
                playScore,
                teamworkScore = validTeamwork,
                r.Mvp,
                highlights = r.Highlights,
                mistakes = r.Mistakes,
                betterLines = r.BetterLines
            });
        }

        McpCallerScope scope = McpCallerScope.Current;
        ValidatedMcpToken caller = scope.Caller;
        string reviewerName = caller.PreferredPlayerName ?? caller.Name;
        string ratingsJson = JsonSerializer.Serialize(normalizedRatings, CompactCamelJson);
        string turningJson = JsonSerializer.Serialize(turningPoints ?? [], CompactCamelJson);
        string patternsJson = JsonSerializer.Serialize(observedPatterns ?? [], CompactCamelJson);

        (bool ok, string msg, DuelReviewEntity? saved) = await deps.Store.SaveDuelReviewAsync(
            duelRecordId: parsedDuelId,
            headline: headline?.Trim() ?? string.Empty,
            summary: summary?.Trim() ?? string.Empty,
            playerRatingsJson: ratingsJson,
            turningPointsJson: turningJson,
            observedPatternsJson: patternsJson,
            reviewerName: reviewerName,
            reviewerRole: string.IsNullOrWhiteSpace(reviewerRole) ? "coach" : reviewerRole.Trim(),
            reviewerTokenId: caller.TokenId,
            cancellationToken: ct).ConfigureAwait(false);

        if (!ok || saved is null)
            return ToolOutcome.Fail(new { success = false, error = "save_review_failed", message = msg });

        Guid? savedMemoryId = null;
        if (!string.IsNullOrWhiteSpace(lessonForMemory))
        {
            AiDuelistMemoryEntity mem = await deps.Store.SaveAiDuelistMemoryAsync(
                caller.TokenId,
                deckHash,
                opponentArchetype,
                opponentName,
                result ?? "note",
                $"[R#{parsedDuelId} 复盘沉淀] {lessonForMemory.Trim()}",
                ct).ConfigureAwait(false);
            savedMemoryId = mem.Id;
        }

        await deps.Store.AppendAuditAsync("mcp_token", caller.TokenId.ToString(), reviewerName, "mcp.duel_submit_review", caller.McpRole, $"R#{parsedDuelId}", scope.RemoteIp, "ok", saved.Headline, ct).ConfigureAwait(false);
        deps.PublishPublicEventCallback?.Invoke("duel.reviewed", new { duelId = parsedDuelId, replayCode = $"R#{parsedDuelId}", saved.Headline, saved.ReviewerName });

        return ToolOutcome.Ok(new
        {
            success = true,
            message = msg,
            savedMemoryId,
            review = DeckAndReplayAppService.FormatDuelReviewDto(saved)
        });
    }

    [McpServerTool(Name = "duel_get_reviews")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("读取指定已完结对局（R#<id>）名下所有已提交的 AI 赛后复盘报告与选手评分。")]
    public async Task<ToolOutcome> DuelGetReviewsAsync(
        [Description("已结束对局编号，如 1 或 'R#1'")] JsonElement? duelId = null,
        CancellationToken ct = default)
    {
        if (!McpArgHelpers.TryParseDuelId(duelId, out long parsedId))
            return ToolOutcome.Fail(new { success = false, message = "请传入有效的已结束对局编号（如 1 或 'R#1'）。" });

        IReadOnlyList<DuelReviewEntity> list = await deps.Store.ListDuelReviewsAsync(parsedId, ct).ConfigureAwait(false);
        return ToolOutcome.Ok(new
        {
            duelId = parsedId,
            replayCode = $"R#{parsedId}",
            count = list.Count,
            reviews = list.Select(DeckAndReplayAppService.FormatDuelReviewDto).ToArray()
        });
    }

    private static string[] CleanStringArray(string[]? arr) =>
        arr is null or { Length: 0 }
            ? []
            : arr.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray();
}
