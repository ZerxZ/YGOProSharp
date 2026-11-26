using System.Text;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;

namespace YGOProSharp.AgentBridge;

public sealed partial class AgentDuelSession
{
    /// <summary>
    /// 按结构化谓词连续执行一组确定性连招步骤（参考 ygo-ai steps：按类型/卡密/标签匹配而非固定易漂移下标；一旦失配立即停驻且不消耗新票据）。
    /// </summary>
    public async Task<AgentActionResult> ExecuteBatchStepsAsync(
        IReadOnlyList<AgentBatchStep> steps,
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        if (steps is null || steps.Count == 0)
        {
            lock (_sync)
                return BuildActionResultLocked(false, "steps 数组不能为空。", null, detail, false, 0, "empty_steps");
        }

        int applied = 0;
        long? lastHandled = null;
        string stopReason = "steps_completed";

        for (int sIdx = 0; sIdx < steps.Count && !cancellationToken.IsCancellationRequested; sIdx++)
        {
            AgentBatchStep step = steps[sIdx];
            int? matchedIdx = null;
            long promptId;

            lock (_sync)
            {
                _client.Update();
                if (_pendingPrompt is null)
                {
                    stopReason = $"no_open_prompt_at_step_{sIdx}";
                    break;
                }

                AgentPendingPrompt p = _pendingPrompt.PublicPrompt;
                promptId = p.PromptId;

                if (!string.IsNullOrWhiteSpace(step.PromptType) &&
                    !string.Equals(p.PromptType, step.PromptType.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    stopReason = $"unmatched_prompt_type_at_step_{sIdx}:{p.PromptType}";
                    break;
                }

                if (step.ActionIndex.HasValue)
                {
                    if (step.ActionIndex.Value >= 0 && step.ActionIndex.Value < p.Choices.Count)
                        matchedIdx = step.ActionIndex.Value;
                }
                else
                {
                    foreach (AgentLegalActionChoice c in p.Choices)
                    {
                        if (step.Cancel && c.Kind is not ("cancel" or "pass_chain" or "no" or "finish"))
                            continue;
                        if (!string.IsNullOrWhiteSpace(step.Kind) && !string.Equals(c.Kind, step.Kind.Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (step.CardCode.HasValue && c.CardCode != step.CardCode.Value)
                            continue;
                        if (!string.IsNullOrWhiteSpace(step.LabelContains) && !c.Label.Contains(step.LabelContains.Trim(), StringComparison.OrdinalIgnoreCase))
                            continue;

                        matchedIdx = c.ActionIndex;
                        break;
                    }
                }

                if (!matchedIdx.HasValue)
                {
                    stopReason = $"unresolved_selector_at_step_{sIdx}";
                    break;
                }
            }

            AgentActionResult stepRes = await SubmitActionAsync(
                matchedIdx,
                null,
                promptId,
                null,
                null,
                AgentObservationDetail.None,
                waitSettleMs: 900,
                cancellationToken).ConfigureAwait(false);

            if (!stepRes.Success)
            {
                stopReason = $"step_{sIdx}_rejected:{stepRes.Message}";
                break;
            }

            applied++;
            lastHandled = stepRes.HandledPromptId;
        }

        lock (_sync)
        {
            _client.Update();
            return BuildActionResultLocked(
                applied > 0,
                $"批量连招执行完成：成功推进 {applied}/{steps.Count} 步（停止原因：{stopReason}）。",
                lastHandled,
                detail,
                advanceCursor: true,
                appliedSteps: applied,
                stopReason: stopReason);
        }
    }

    /// <summary>
    /// 服务端内联托管推进若干常规步骤（如自动跳过无打算发动的自由时点，或执行基础强攻策略），避免客户端高频往返。
    /// </summary>
    public async Task<AgentActionResult> AutoPlayStepsAsync(
        int maxDecisions = 8,
        string strategy = "pass_chains",
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        int cap = Math.Clamp(maxDecisions, 1, 20);
        string mode = (strategy ?? "pass_chains").Trim().ToLowerInvariant();
        int applied = 0;
        long? lastHandled = null;
        string stopReason = "max_decisions_reached";

        for (int i = 0; i < cap && !cancellationToken.IsCancellationRequested; i++)
        {
            int? pickSingle = null;
            int[]? pickMulti = null;
            long promptId = 0;

            lock (_sync)
            {
                _client.Update();
                if (_state is AgentSessionState.DuelEnded or AgentSessionState.Disconnected)
                {
                    stopReason = "duel_ended";
                    break;
                }

                if (_pendingPrompt is null)
                {
                    stopReason = "waiting_for_opponent";
                    break;
                }

                AgentPendingPrompt p = _pendingPrompt.PublicPrompt;
                promptId = p.PromptId;

                if (p.Choices.Count == 1 && p.MinSelect <= 1 && p.MaxSelect <= 1)
                {
                    pickSingle = 0;
                }
                else if (p.PromptType == "select_chain" && p.CanCancel)
                {
                    pickSingle = 0; // Pass
                }
                else if (mode == "simple_beatdown")
                {
                    if (p.PromptType == "rock_paper_scissors")
                        pickSingle = Random.Shared.Next(3);
                    else if (p.PromptType == "select_turn_order")
                        pickSingle = 1; // 后攻打点流默认抢血或先攻均可
                    else if (p.PromptType == "select_idle_cmd")
                    {
                        var bestSummon = p.Choices
                            .Where(static c => c.Kind is "spsummon" or "summon")
                            .OrderByDescending(static c => c.Attack ?? 0)
                            .FirstOrDefault();
                        var toBp = p.Choices.FirstOrDefault(static c => c.Kind == "to_battle_phase");
                        var toEp = p.Choices.FirstOrDefault(static c => c.Kind == "to_end_phase");
                        if (bestSummon is not null && _self.MonsterZone.Count(static m => m is not null) < 5)
                            pickSingle = bestSummon.ActionIndex;
                        else if (toBp is not null && _self.MonsterZone.Any(static m => m is not null && (m.Position & (int)CardPosition.Attack) != 0))
                            pickSingle = toBp.ActionIndex;
                        else if (toEp is not null)
                            pickSingle = toEp.ActionIndex;
                    }
                    else if (p.PromptType == "select_battle_cmd")
                    {
                        var atk = p.Choices.FirstOrDefault(static c => c.Kind == "attack");
                        var ep = p.Choices.FirstOrDefault(static c => c.Kind is "to_m2" or "to_end_phase");
                        pickSingle = atk?.ActionIndex ?? ep?.ActionIndex;
                    }
                    else if (p.PromptType == "select_position")
                    {
                        pickSingle = 0;
                    }
                    else if (p.PromptType == "select_yes_no")
                    {
                        pickSingle = 0;
                    }
                    else if (p.PromptType is "select_card" or "select_place" && p.MinSelect >= 1 && p.Choices.Count >= p.MinSelect)
                    {
                        pickMulti = Enumerable.Range(0, p.MinSelect).ToArray();
                    }
                }

                if (!pickSingle.HasValue && pickMulti is null)
                {
                    stopReason = $"decision_required:{p.PromptType}";
                    break;
                }
            }

            AgentActionResult stepRes = await SubmitActionAsync(
                pickSingle,
                pickMulti,
                promptId,
                null,
                null,
                AgentObservationDetail.None,
                waitSettleMs: 900,
                cancellationToken).ConfigureAwait(false);

            if (!stepRes.Success)
            {
                stopReason = $"auto_step_failed:{stepRes.Message}";
                break;
            }

            applied++;
            lastHandled = stepRes.HandledPromptId;
        }

        lock (_sync)
        {
            _client.Update();
            return BuildActionResultLocked(
                true,
                $"自动推进已执行 {applied} 步（停止于：{stopReason}）。",
                lastHandled,
                detail,
                advanceCursor: true,
                appliedSteps: applied,
                stopReason: stopReason);
        }
    }

    private async Task WaitPostActionSettleAsync(long revBefore, int maxWaitMs, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(maxWaitMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            lock (_sync)
            {
                _client.Update();
                if (_pendingPrompt is not null ||
                    _state is AgentSessionState.DuelEnded or AgentSessionState.SideDecking or AgentSessionState.Disconnected ||
                    _lastError is not null)
                {
                    return;
                }

                // 若服务端已回传了本次动作引发的移动/召唤/回合切换消息，且连续 28ms 无新包，说明已交由对手思考
                if (_stateRevision > revBefore && (DateTime.UtcNow - _lastPacketReceivedUtc).TotalMilliseconds >= 28)
                    return;
            }

            await Task.Delay(4, ct).ConfigureAwait(false);
        }
    }
}
