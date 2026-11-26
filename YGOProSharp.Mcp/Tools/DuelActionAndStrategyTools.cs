using System.ComponentModel;
using ModelContextProtocol.Server;
using YGOProSharp.AgentBridge;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Mcp.Tools;

public sealed record PlanUpdateInput(
    string? WinCondition = null,
    string? SuspectedOpponentDeck = null,
    string? TurnGoal = null,
    string? HandoffToPartner = null,
    string? Bo3SidePlan = null);

/// <summary>
/// 对局动作提交、多步批处理、自动响应策略、长线计划、2v2《卡片力量》搭档战术同步与跨局生涯记忆工具。
/// </summary>
public sealed class DuelActionAndStrategyTools(McpRuntimeDependencies deps)
{
    [McpServerTool(Name = "duel_submit_action")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("提交所选合法动作并等待服务端 ocgcore 完成结算后再返回最新快照：单选传 actionIndex，多张选卡/祭品/凑星传 cardIndices 数组（单选两者自动双向兼容）；支持 expectedPromptId/promptId 非销毁式票据防重、chatLine 顺带吐槽与 planUpdate 同步长线计划。")]
    public async Task<ToolOutcome> DuelSubmitActionAsync(
        string sessionId,
        [Description("单选合法动作编号（单祭品/单张选卡也可直接传）")] int? actionIndex = null,
        [Description("多张选卡、多祭品或凑星时的候选下标数组（如 [0, 1]）")] int[]? cardIndices = null,
        [Description("cardIndices 的兼容别名")] int[]? indices = null,
        [Description("可选：校验当前询问编号防止过期重复提交")] long? promptId = null,
        [Description("promptId 的兼容别名（不匹配时仅返回当前真询问而不消耗票据）")] long? expectedPromptId = null,
        [Description("返回快照详略级别（none|minimal|standard|full，默认 standard）")] string? detail = null,
        [Description("可选：公开展示给观众的简短思考理由（勿写暗手牌机密）")] string? thoughtSummary = null,
        [Description("可选：顺带发送到房间聊天框的台词")] string? chatMessage = null,
        [Description("chatMessage 别名：关键出招时贴合人设的简短发言（勿泄露暗牌）")] string? chatLine = null,
        [Description("可选：顺带更新跨回合战术计划")] PlanUpdateInput? planUpdate = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        if (planUpdate is not null)
        {
            s.UpdateStrategicPlan(
                planUpdate.WinCondition,
                planUpdate.SuspectedOpponentDeck,
                planUpdate.TurnGoal,
                planUpdate.HandoffToPartner,
                planUpdate.Bo3SidePlan);
        }

        int[]? effectiveIndices = cardIndices ?? indices;
        long? effectivePromptId = expectedPromptId ?? promptId;
        string? effectiveChat = !string.IsNullOrWhiteSpace(chatLine) ? chatLine : chatMessage;
        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);

        AgentActionResult res = await s.SubmitActionAsync(
            actionIndex,
            effectiveIndices,
            effectivePromptId,
            thoughtSummary,
            effectiveChat,
            obsDetail,
            waitSettleMs: 850,
            cancellationToken: ct).ConfigureAwait(false);
        return ToolOutcome.From(res.Success, res);
    }

    [McpServerTool(Name = "duel_submit_batch")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在单次请求内按谓词连续执行多步确定性操作（如：发动某卡 -> 选目标 -> 自动放弃连锁），一旦遇到未预期分支或对方连锁立即安全停驻并返回当前询问。")]
    public async Task<ToolOutcome> DuelSubmitBatchAsync(
        string sessionId,
        [Description("顺序匹配步骤列表（最多 16 步）")] AgentBatchStep[] steps,
        string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        if (steps is null || steps.Length == 0)
            return ToolOutcome.Fail(new { message = "steps 数组不能为空。" });

        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        AgentActionResult res = await s.ExecuteBatchStepsAsync(steps.Take(16).ToList(), obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.From(res.Success, res);
    }

    [McpServerTool(Name = "duel_auto_play")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("快速连打/自动代步工具：支持 strategy='pass_chains'（自动跳过所有非必发连锁窗口直至轮到主要决策）或 'simple_beatdown'（自动通召最高攻怪兽、进战阶攻击并结束回合）。")]
    public async Task<ToolOutcome> DuelAutoPlayAsync(
        string sessionId,
        [Description("自动策略（pass_chains|simple_beatdown，默认 pass_chains）")] string strategy = "pass_chains",
        [Description("最多自动执行步数 (1~20，默认 8)")] int maxSteps = 8,
        string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        string effStrategy = string.IsNullOrWhiteSpace(strategy) ? "pass_chains" : strategy.Trim();
        int clampedSteps = Math.Clamp(maxSteps, 1, 20);
        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        AgentActionResult res = await s.AutoPlayStepsAsync(clampedSteps, effStrategy, obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.From(res.Success, res);
    }

    [McpServerTool(Name = "duel_configure_policy")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("配置当前对局会话的快速响应策略：如开启唯一合法选项自动确认、同阶段重复连锁窗口自动折叠，或静默跳过接下来 N 次可取消连锁询问。")]
    public ToolOutcome DuelConfigurePolicy(
        string sessionId,
        [Description("仅剩唯一合法选项时是否自动确认（默认 true）")] bool? autoConfirmSingleForced = null,
        [Description("同一阶段同一张卡 Pass 后是否自动跳过重复询问（默认 true）")] bool? suppressRepeatedChainPass = null,
        [Description("接下来自动放弃的可取消连锁询问次数 (0~100)")] int? silenceChainPrompts = null)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        s.ConfigurePolicy(autoConfirmSingleForced, suppressRepeatedChainPass, silenceChainPrompts);
        return ToolOutcome.Ok(new { success = true, message = "已更新会话自动响应策略。" });
    }

    [McpServerTool(Name = "duel_update_plan")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("更新当前会话的跨回合战术备忘录（保存在会话观测 strategicPlan 中，帮助跨回合保持长线计划）。")]
    public ToolOutcome DuelUpdatePlan(
        string sessionId,
        string? winCondition = null,
        string? suspectedOpponentDeck = null,
        string? turnGoal = null,
        string? handoffToPartner = null,
        string? bo3SidePlan = null)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentStrategicPlan plan = s.UpdateStrategicPlan(winCondition, suspectedOpponentDeck, turnGoal, handoffToPartner, bo3SidePlan);
        return ToolOutcome.Ok(new { success = true, strategicPlan = plan });
    }

    [McpServerTool(Name = "duel_sync_partner_tactic")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在 2v2 双打模式下向同队搭档同步《卡片力量》式战术姿态与共享盘面交接说明（仅己方队伍可见，不向敌方或公共观战流公开）。")]
    public ToolOutcome DuelSyncPartnerTactic(
        string sessionId,
        [Description("aggressive|all_in_otk|control|setup_partner|defensive|balanced")] string? stance = null,
        [Description("简短战术意图（如『下回合交给你斩杀』）")] string? note = null,
        [Description("回合交接给搭档的场上盖卡/怪兽/墓地续航说明")] string? sharedBoardHandoffNote = null)
    {
        return SyncPartnerInternal(sessionId, stance, note, sharedBoardHandoffNote);
    }

    [McpServerTool(Name = "duel_tag_sync")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("《卡片力量（Tag Force）》2v2 双打同队私密战术协同（duel_sync_partner_tactic 快捷入口）：同步战术姿态与回合结束盘面交接备忘，自动唤醒同队 AI 搭档且绝不向对手剧透。")]
    public ToolOutcome DuelTagSync(
        string sessionId,
        [Description("aggressive|all_in_otk|control|setup_partner|defensive|balanced")] string? stance = null,
        string? note = null,
        string? sharedBoardHandoffNote = null)
    {
        return SyncPartnerInternal(sessionId, stance, note, sharedBoardHandoffNote);
    }

    [McpServerTool(Name = "duel_save_memory")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("将实战领悟到的对局经验、特定卡组打法或针对某对手的突破心得持久化存入当前 AI Token 的跨局生涯记忆库（自动保留最新 50 条）。")]
    public async Task<ToolOutcome> DuelSaveMemoryAsync(
        [Description("提炼出的实战经验（最长550字）")] string strategicNote,
        [Description("可选：关联己方卡组 Hash")] string? deckHash = null,
        [Description("可选：对手卡组主题")] string? opponentArchetype = null,
        [Description("可选：对手决斗者昵称")] string? opponentName = null,
        [Description("win|loss|draw|note")] string? result = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(strategicNote))
            return ToolOutcome.Fail(new { success = false, message = "strategicNote 不能为空。" });

        AiDuelistMemoryEntity saved = await deps.Store.SaveAiDuelistMemoryAsync(
            McpCallerScope.Current.Caller.TokenId,
            deckHash,
            opponentArchetype,
            opponentName,
            result,
            strategicNote.Trim(),
            ct).ConfigureAwait(false);

        return ToolOutcome.Ok(new
        {
            success = true,
            memoryId = saved.Id,
            saved.DeckHash,
            saved.OpponentArchetype,
            saved.OpponentName,
            saved.Result,
            saved.StrategicNote,
            saved.CreatedAt
        });
    }

    [McpServerTool(Name = "duel_get_memories")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("按己方卡组 Hash 或对手关键词检索当前 AI Token 积累的跨局实战生涯记忆（开局前或换副卡组时调用可显著提升针对性）。")]
    public async Task<ToolOutcome> DuelGetMemoriesAsync(
        string? deckHash = null,
        [Description("可选：按对手昵称、对手卡组名或笔记关键词过滤")] string? opponentKeyword = null,
        [Description("返回条数 (1~50，默认 10)")] int limit = 10,
        CancellationToken ct = default)
    {
        int clamped = Math.Clamp(limit, 1, 50);
        IReadOnlyList<AiDuelistMemoryEntity> memories = await deps.Store.QueryAiDuelistMemoriesAsync(
            McpCallerScope.Current.Caller.TokenId,
            deckHash,
            opponentKeyword,
            clamped,
            ct).ConfigureAwait(false);

        return ToolOutcome.Ok(new
        {
            count = memories.Count,
            memories = memories.Select(static m => new
            {
                m.Id,
                m.DeckHash,
                m.OpponentArchetype,
                m.OpponentName,
                m.Result,
                m.StrategicNote,
                m.CreatedAt
            })
        });
    }

    private ToolOutcome SyncPartnerInternal(string sessionId, string? stance, string? note, string? sharedBoardHandoffNote)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        (bool syncOk, string msg, AgentPartnerTactic? tactic) = s.SyncPartnerTactic(
            stance,
            note,
            sendToTeamChat: true,
            sharedBoardHandoffNote: sharedBoardHandoffNote);
        return ToolOutcome.From(syncOk, new { success = syncOk, message = msg, partnerTactic = tactic });
    }
}
