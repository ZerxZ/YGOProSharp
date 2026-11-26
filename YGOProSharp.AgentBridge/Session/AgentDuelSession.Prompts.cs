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
    private void ParseSelectIdleCmdLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte(); // player
        List<AgentLegalActionChoice> choices = new();
        List<(int RawValue, string Desc)> actions = new();

        string[] categoryKind = ["summon", "spsummon", "repos", "set_monster", "set_spell"];
        string[] categoryVerb = ["通常召唤", "特殊召唤", "改变表示形式", "里侧守备放置怪兽", "盖放魔陷"];

        for (int k = 0; k < 5; k++)
        {
            int count = packet.ReadByte();
            for (int i = 0; i < count; i++)
            {
                int code = packet.ReadInt32();
                int con = GetLocalPlayer(packet.ReadByte());
                CardLocation loc = (CardLocation)packet.ReadByte();
                int seq = packet.ReadByte();
                TrackedCard? tracked = GetSide(con).GetCard(loc, seq);
                int realCode = code != 0 ? code : (tracked?.Code ?? 0);
                (string name, int? atk, int? def, string? shortEff) = LookupCardBrief(repo, realCode, tracked);
                int rawVal = (i << 16) + k;
                string label = $"{categoryVerb[k]}：{name} ({loc}[{seq}])";
                choices.Add(new AgentLegalActionChoice(choices.Count, categoryKind[k], label, realCode, name, $"{loc}[{seq}]", null, atk, def, shortEff));
                actions.Add((rawVal, label));
            }
        }

        int actCount = packet.ReadByte();
        for (int i = 0; i < actCount; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            int desc = packet.ReadInt32();
            TrackedCard? tracked = GetSide(con).GetCard(loc, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            (string name, int? atk, int? def, string? shortEff) = LookupCardBrief(repo, realCode, tracked);
            string? effHint = ResolveEffectDescription(repo, desc, realCode);
            int rawVal = (i << 16) + 5;
            string label = string.IsNullOrEmpty(effHint)
                ? $"发动效果：{name} ({loc}[{seq}])"
                : $"发动效果：{name} ({loc}[{seq}] — {effHint})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "activate", label, realCode, name, $"{loc}[{seq}]", desc, atk, def, shortEff, effHint));
            actions.Add((rawVal, label));
        }

        bool canBp = packet.ReadByte() != 0;
        bool canEp = packet.ReadByte() != 0;
        packet.ReadByte(); // canShuffle

        if (canBp)
        {
            const string bpLabel = "进入战斗阶段 (Battle Phase)";
            choices.Add(new AgentLegalActionChoice(choices.Count, "to_battle_phase", bpLabel));
            actions.Add((6, bpLabel));
        }

        if (canEp || choices.Count == 0)
        {
            const string epLabel = "结束回合 (End Phase)";
            choices.Add(new AgentLegalActionChoice(choices.Count, "to_end_phase", epLabel));
            actions.Add((7, epLabel));
        }

        SetPendingPromptLocked(
            "select_idle_cmd",
            $"主要阶段空闲操作（Turn {_turnCount} {_phase}）",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= actions.Count)
                    return (false, $"无效的 actionIndex，请在 0 ~ {actions.Count - 1} 之间选择。");
                SendResponseInt32Locked(actions[idx.Value].RawValue);
                return (true, actions[idx.Value].Desc);
            });
    }

    private void ParseSelectBattleCmdLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        List<AgentLegalActionChoice> choices = new();
        List<(int RawValue, string Desc)> actions = new();

        int actCount = packet.ReadByte();
        for (int i = 0; i < actCount; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            int desc = packet.ReadInt32();
            TrackedCard? tracked = GetSide(con).GetCard(loc, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            (string name, int? atk, int? def, string? shortEff) = LookupCardBrief(repo, realCode, tracked);
            string? effHint = ResolveEffectDescription(repo, desc, realCode);
            string label = string.IsNullOrEmpty(effHint)
                ? $"发动战斗阶段效果：{name} ({loc}[{seq}])"
                : $"发动战斗阶段效果：{name} ({loc}[{seq}] — {effHint})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "activate", label, realCode, name, $"{loc}[{seq}]", desc, atk, def, shortEff, effHint));
            actions.Add(((i << 16) + 0, label));
        }

        int atkCount = packet.ReadByte();
        for (int i = 0; i < atkCount; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            bool dirAtt = packet.ReadByte() != 0;
            TrackedCard? tracked = GetSide(con).GetCard(loc, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            (string name, int? atk, int? def, _) = LookupCardBrief(repo, realCode, tracked);
            string atkInfo = atk is not null ? $" ATK={atk}" : string.Empty;
            string label = $"宣言攻击：{name} ({loc}[{seq}]{atkInfo}{(dirAtt ? ", 可直接攻击" : "")})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "attack", label, realCode, name, $"{loc}[{seq}]", null, atk, def));
            actions.Add(((i << 16) + 1, label));
        }

        bool canM2 = packet.ReadByte() != 0;
        bool canEp = packet.ReadByte() != 0;
        if (canM2)
        {
            const string m2Label = "进入主要阶段 2 (Main Phase 2)";
            choices.Add(new AgentLegalActionChoice(choices.Count, "to_m2", m2Label));
            actions.Add((2, m2Label));
        }
        if (canEp || choices.Count == 0)
        {
            const string epLabel = "结束战斗并进入结束阶段 (End Phase)";
            choices.Add(new AgentLegalActionChoice(choices.Count, "to_end_phase", epLabel));
            actions.Add((3, epLabel));
        }

        SetPendingPromptLocked(
            "select_battle_cmd",
            $"战斗阶段操作选择（Turn {_turnCount}）",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= actions.Count)
                    return (false, $"无效的 actionIndex，请在 0 ~ {actions.Count - 1} 之间选择。");
                SendResponseInt32Locked(actions[idx.Value].RawValue);
                return (true, actions[idx.Value].Desc);
            });
    }

    private void ParseSelectChainLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte(); // player
        int count = packet.ReadByte();
        packet.ReadByte(); // specount
        packet.ReadInt32(); // hint1
        packet.ReadInt32(); // hint2

        List<(int ChainIdx, int Code, string Name, string Zone, int Desc, bool Forced, string? EffText, string? ShortEff)> items = new(count);
        for (int i = 0; i < count; i++)
        {
            packet.ReadByte(); // flag
            bool forced = packet.ReadByte() != 0;
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            packet.ReadByte(); // sseq
            int desc = packet.ReadInt32();
            TrackedCard? tracked = GetSide(con).GetCard(loc, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            (string name, _, _, string? shortEff) = LookupCardBrief(repo, realCode, tracked);
            string? effText = ResolveEffectDescription(repo, desc, realCode);
            items.Add((i, realCode, name, $"{(con == 0 ? "我方" : "对方")}.{loc}[{seq}]", desc, forced, effText, shortEff));
        }

        if (items.Count == 0)
        {
            SendResponseInt32Locked(-1);
            return;
        }

        if (items.Count == 1 && items[0].Forced)
        {
            SendResponseInt32Locked(0);
            AddEventLocked($"[fast] 必发连锁自动确认：{items[0].Name}");
            return;
        }

        bool anyForced = items.Any(static x => x.Forced);
        string chainSig = string.Join(",", items.Select(static x => $"{x.Code}:{x.Zone}:{x.Desc}").OrderBy(static s => s, StringComparer.Ordinal));

        // 静默计数或同阶段重复连锁窗口自动跳过（不占外部 AI 往返预算）
        if (!anyForced)
        {
            if (_silenceChainRemaining > 0)
            {
                _silenceChainRemaining--;
                SendResponseInt32Locked(-1);
                AddEventLocked($"[silence] 自动放弃连锁响应（剩余静默 {_silenceChainRemaining} 次）");
                return;
            }

            if (_suppressRepeatedChainPass &&
                _echoedChainTurn == _turnCount &&
                _echoedChainPhase == _phase &&
                string.Equals(_echoedChainSignature, chainSig, StringComparison.Ordinal))
            {
                SendResponseInt32Locked(-1);
                AddEventLocked("[echo] 同阶段相同可取消连锁窗口已内联自动放弃");
                return;
            }
        }

        List<AgentLegalActionChoice> choices = new();
        List<(int RawResponse, string Desc)> map = new();

        if (!anyForced)
        {
            choices.Add(new AgentLegalActionChoice(0, "pass_chain", "不连锁 / 放弃时点 (Pass)"));
            map.Add((-1, "不连锁 (Pass)"));
        }

        foreach (var item in items)
        {
            string effSuffix = !string.IsNullOrEmpty(item.EffText) ? $" — {item.EffText}" : string.Empty;
            string label = $"连锁发动：{item.Name} ({item.Zone}{(item.Forced ? ", 必发" : "")}){effSuffix}";
            choices.Add(new AgentLegalActionChoice(choices.Count, "chain_activate", label, item.Code, item.Name, item.Zone, item.Desc, null, null, item.ShortEff, item.EffText));
            map.Add((item.ChainIdx, label));
        }

        string triggerContext;
        if (_chain.Count > 0)
        {
            AgentChainLinkView top = _chain[^1];
            string topSide = top.Side == "self" ? "我方" : "对方";
            string topEff = string.IsNullOrEmpty(top.EffectText) ? string.Empty : $" [{top.EffectText}]";
            triggerContext = $"连锁响应询问：当前待组 Chain {_chain.Count + 1}（上一环 Chain {top.ChainIndex} 为{topSide}发动「{top.CardName}」@{top.Location}{topEff}）";
        }
        else
        {
            string latestEvent = _eventLedger.Count > 0 ? _eventLedger[^1].Text : $"{_phase}";
            triggerContext = $"自由时点/诱发询问（当前无连锁，Turn {_turnCount} {_phase}；最近场面事件：{latestEvent}）";
        }

        SetPendingPromptLocked(
            "select_chain",
            triggerContext,
            canCancel: !anyForced,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= map.Count)
                    return (false, $"无效的 actionIndex，请在 0 ~ {map.Count - 1} 之间选择。");
                SendResponseInt32Locked(map[idx.Value].RawResponse);
                return (true, map[idx.Value].Desc);
            },
            chainSignature: chainSig);
    }

    private void ParseSelectEffectYnLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int code = packet.ReadInt32();
        int con = GetLocalPlayer(packet.ReadByte());
        CardLocation loc = (CardLocation)packet.ReadByte();
        int seq = packet.ReadByte();
        packet.ReadByte();
        int desc = packet.ReadInt32();
        (string name, _, _, string? shortEff) = LookupCardBrief(repo, code, null);
        string? effText = ResolveEffectDescription(repo, desc, code);
        string effSuffix = string.IsNullOrEmpty(effText) ? string.Empty : $"：{effText}";

        SetPendingPromptLocked(
            "select_effect_yn",
            $"是否发动「{name}」({(con == 0 ? "我方" : "对方")}.{loc}[{seq}]) 的效果{effSuffix}？",
            canCancel: true,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            [
                new AgentLegalActionChoice(0, "yes", $"是：发动「{name}」效果{effSuffix}", code, name, $"{con}:{loc}[{seq}]", desc, null, null, shortEff, effText),
                new AgentLegalActionChoice(1, "no", $"否：不发动「{name}」效果", code, name)
            ],
            (idx, _) =>
            {
                if (idx is null or < 0 or > 1)
                    return (false, "请选择 0（发动）或 1（不发动）。");
                SendResponseInt32Locked(idx.Value == 0 ? 1 : 0);
                return (true, idx.Value == 0 ? $"发动 {name}" : $"不发动 {name}");
            });
    }

    private void ParseSelectYesNoLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int desc = packet.ReadInt32();
        string? decoded = ResolveEffectDescription(repo, desc, 0);
        string hint = !string.IsNullOrEmpty(decoded)
            ? $"确认是/否：{decoded}"
            : $"确认是/否询问 (desc={desc})";

        SetPendingPromptLocked(
            "select_yes_no",
            hint,
            canCancel: true,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            [
                new AgentLegalActionChoice(0, "yes", string.IsNullOrEmpty(decoded) ? "是 (Yes)" : $"是：{decoded}", EffectDescription: desc, EffectText: decoded),
                new AgentLegalActionChoice(1, "no", "否 (No)")
            ],
            (idx, _) =>
            {
                if (idx is null or < 0 or > 1)
                    return (false, "请选择 0（是）或 1（否）。");
                SendResponseInt32Locked(idx.Value == 0 ? 1 : 0);
                return (true, idx.Value == 0 ? "选择：是" : "选择：否");
            });
    }

    private void ParseSelectPositionLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int code = packet.ReadInt32();
        int mask = packet.ReadByte();
        string name = LookupCardName(repo, code);

        List<AgentLegalActionChoice> choices = new();
        List<(int PosVal, string Label)> options = new();
        (CardPosition Pos, string Label)[] all =
        [
            (CardPosition.FaceUpAttack, "表侧攻击表示 (FaceUpAttack)"),
            (CardPosition.FaceDownAttack, "里侧攻击表示 (FaceDownAttack)"),
            (CardPosition.FaceUpDefence, "表侧守备表示 (FaceUpDefence)"),
            (CardPosition.FaceDownDefence, "里侧守备表示 (FaceDownDefence)")
        ];
        foreach ((CardPosition pos, string label) in all)
        {
            if ((mask & (int)pos) != 0)
            {
                choices.Add(new AgentLegalActionChoice(choices.Count, "position", $"{name} -> {label}", code, name));
                options.Add(((int)pos, label));
            }
        }

        if (options.Count == 1)
        {
            SendResponseInt32Locked(options[0].PosVal);
            AddEventLocked($"[fast] 唯一表示形式自动选择：{name} -> {options[0].Label}");
            return;
        }

        SetPendingPromptLocked(
            "select_position",
            $"请选择「{name}」的表示形式",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= options.Count)
                    return (false, $"请选择 0 ~ {options.Count - 1}。");
                SendResponseInt32Locked(options[idx.Value].PosVal);
                return (true, $"{name}: {options[idx.Value].Label}");
            });
    }

    private void ParseSelectOptionLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        List<AgentLegalActionChoice> choices = new(count);
        for (int i = 0; i < count; i++)
        {
            int optCode = packet.ReadInt32();
            string? effText = ResolveEffectDescription(repo, optCode, 0);
            string label = string.IsNullOrEmpty(effText)
                ? $"选项 #{i} (desc={optCode})"
                : $"选项 #{i}：{effText}";
            choices.Add(new AgentLegalActionChoice(i, "option", label, EffectDescription: optCode, EffectText: effText));
        }

        SetPendingPromptLocked(
            "select_option",
            "请选择一项要适用的效果分支选项",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= count)
                    return (false, $"请选择 0 ~ {count - 1}。");
                SendResponseInt32Locked(idx.Value);
                return (true, choices[idx.Value].Label);
            });
    }

    private void ParseSelectCardLocked(BinaryReader packet, INamedCardRepository repo, bool isTribute)
    {
        packet.ReadByte();
        bool cancelable = packet.ReadByte() != 0;
        int min = packet.ReadByte();
        int max = packet.ReadByte();
        int count = packet.ReadByte();

        List<(int Code, string Name, string Zone, int TributeWeight)> candidates = new(count);
        List<AgentLegalActionChoice> choices = new(count + 1);

        for (int i = 0; i < count; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            int param = packet.ReadByte();
            TrackedCard? tracked = GetSide(con).GetCard(loc & ~CardLocation.Overlay, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            (string name, int? atk, int? def, string? shortEff) = realCode > 0
                ? LookupCardBrief(repo, realCode, tracked)
                : ("里侧未知卡", null, null, null);
            string zone = $"{(con == 0 ? "我方" : "对方")}.{loc}[{seq}]";
            int weight = Math.Max(1, param);
            string tribSuffix = isTribute && weight > 1 ? $", 可当{weight}祭品" : string.Empty;
            candidates.Add((realCode, name, zone, weight));
            choices.Add(new AgentLegalActionChoice(i, isTribute ? "tribute_card" : "card", $"[{i}] {name} ({zone}{tribSuffix})", realCode > 0 ? realCode : null, name, zone, null, atk, def, shortEff));
        }

        int cancelActionIdx = -1;
        if (cancelable)
        {
            cancelActionIdx = choices.Count;
            choices.Add(new AgentLegalActionChoice(cancelActionIdx, "cancel", "取消 / 不选 (Cancel)"));
        }

        int hint = _selectHint;
        _selectHint = 0;
        string? hintDesc = hint != 0 ? ResolveEffectDescription(repo, hint, 0) : null;
        string purpose = !string.IsNullOrEmpty(hintDesc)
            ? hintDesc
            : (isTribute ? "选择上级召唤解放的祭品怪兽" : "按效果要求选择卡片");
        string fieldGuide = max > 1
            ? $"请传 cardIndices 数组（如 [{string.Join(", ", Enumerable.Range(0, Math.Min(min, candidates.Count)))}]）"
            : "可传 actionIndex 或 cardIndices: [i]";

        SetPendingPromptLocked(
            isTribute ? "select_tribute" : "select_card",
            $"{purpose}：需选 {min} ~ {max} 张（{fieldGuide}）",
            canCancel: cancelable,
            minSelect: min,
            maxSelect: max,
            supportsMulti: max > 1,
            choices,
            (idx, multi) =>
            {
                if (cancelable && idx == cancelActionIdx && (multi is null || multi.Count <= 1))
                {
                    SendResponseInt32Locked(-1);
                    return (true, "取消选卡");
                }

                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count == 1 && cancelable && picked[0] == cancelActionIdx)
                {
                    SendResponseInt32Locked(-1);
                    return (true, "取消选卡");
                }

                if (picked.Count == 0 && cancelable)
                {
                    SendResponseInt32Locked(-1);
                    return (true, "取消选卡");
                }

                if (picked.Any(x => x < 0 || x >= candidates.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"选卡下标必须在 0 ~ {candidates.Count - 1} 之间且不重复。");

                if (!isTribute && (picked.Count < min || picked.Count > max))
                    return (false, $"本次必须选择 {min} ~ {max} 张卡（当前只传了 {picked.Count} 张；多选请使用 cardIndices: [0, 1] 数组字段提交，未消耗本次询问）。");

                if (isTribute)
                {
                    int weightSum = picked.Sum(i => candidates[i].TributeWeight);
                    if (weightSum < min || picked.Count > max)
                        return (false, $"上级召唤需要至少 {min} 个祭品（当前所选仅提供 {weightSum} 个祭品；请使用 cardIndices: [0, 1] 数组一次传入多只怪兽下标，或选择 [{cancelActionIdx}] 取消）。");
                }

                byte[] buf = new byte[picked.Count + 1];
                buf[0] = (byte)picked.Count;
                for (int i = 0; i < picked.Count; i++)
                    buf[i + 1] = (byte)picked[i];
                SendResponseBytesLocked(buf);
                return (true, $"选卡：{string.Join(", ", picked.Select(i => candidates[i].Name))}");
            });
    }

    private void ParseSelectUnselectLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        bool finishable = packet.ReadByte() != 0;
        bool cancelable = packet.ReadByte() != 0 || finishable;
        packet.ReadByte(); // min
        packet.ReadByte(); // max

        int count1 = packet.ReadByte();
        List<AgentLegalActionChoice> choices = new();
        List<(int RawValue, string Desc)> map = new();

        for (int i = 0; i < count1; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            packet.ReadByte();
            TrackedCard? tracked = GetSide(con).GetCard(loc & ~CardLocation.Overlay, seq);
            int realCode = code != 0 ? code : (tracked?.Code ?? 0);
            string name = realCode > 0 ? LookupCardName(repo, realCode) : "里侧未知卡";
            string zone = $"{(con == 0 ? "我方" : "对方")}.{loc}[{seq}]";
            string label = $"选择加入：{name} ({zone})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "select_card", label, realCode > 0 ? realCode : null, name, zone));
            map.Add((i, label));
        }

        int count2 = packet.ReadByte();
        for (int i = 0; i < count2; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            packet.ReadByte();
            string name = code > 0 ? LookupCardName(repo, code) : "已选卡";
            string zone = $"{(con == 0 ? "我方" : "对方")}.{loc}[{seq}]";
            string label = $"取消已选：{name} ({zone})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "unselect_card", label, code > 0 ? code : null, name, zone));
            map.Add((count1 + i, label));
        }

        if (finishable || (cancelable && count2 > 0) || choices.Count == 0)
        {
            const string finishLabel = "完成当前组合选择 (Finish)";
            choices.Add(new AgentLegalActionChoice(choices.Count, "finish", finishLabel));
            map.Add((-1, finishLabel));
        }

        SetPendingPromptLocked(
            "select_unselect",
            "逐步选卡（每次提交 1 个 actionIndex，满足条件后可选择 Finish 完成）",
            canCancel: finishable || cancelable,
            minSelect: finishable ? 0 : 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= map.Count)
                    return (false, $"请选择 0 ~ {map.Count - 1}。");
                int raw = map[idx.Value].RawValue;
                if (raw < 0)
                    SendResponseInt32Locked(-1);
                else
                    SendResponseBytesLocked([1, (byte)raw]);
                return (true, map[idx.Value].Desc);
            });
    }

    private void ParseSelectSumLocked(BinaryReader packet, INamedCardRepository repo)
    {
        bool exact = packet.ReadByte() == 0;
        packet.ReadByte();
        int targetSum = packet.ReadInt32();
        int min = packet.ReadByte();
        int max = packet.ReadByte();
        if (max <= 0) max = 99;

        int mustCount = packet.ReadByte();
        int mustSum = 0;
        for (int i = 0; i < mustCount; i++)
        {
            packet.ReadInt32();
            packet.ReadBytes(3);
            uint op = packet.ReadUInt32();
            mustSum += (int)(op & 0xffff);
        }

        int optCount = packet.ReadByte();
        List<(int Code, string Name, int Val1, int Val2)> opts = new(optCount);
        List<AgentLegalActionChoice> choices = new(optCount);
        for (int i = 0; i < optCount; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            uint op = packet.ReadUInt32();
            int v1 = (int)(op & 0xffff);
            int v2 = (int)((op >> 16) & 0xffff);
            string name = LookupCardName(repo, code);
            string label = $"[{i}] {name} ({(con == 0 ? "我方" : "对方")}.{loc}[{seq}], 等级/数值={v1}{(v2 > 0 ? $"/{v2}" : "")})";
            opts.Add((code, name, v1, v2));
            choices.Add(new AgentLegalActionChoice(i, "sum_card", label, code, name));
        }

        _selectHint = 0;

        SetPendingPromptLocked(
            "select_sum",
            $"请选择卡片使等级/数值之和{(exact ? "等于" : "至少达到")} {targetSum - mustSum}（请使用 cardIndices 数组提交下标）",
            canCancel: false,
            minSelect: Math.Max(1, min),
            maxSelect: max,
            supportsMulti: true,
            choices,
            (idx, multi) =>
            {
                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count == 0 || picked.Any(x => x < 0 || x >= opts.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"请通过 cardIndices 数组提供 0 ~ {opts.Count - 1} 的不重复下标。");

                byte[] buf = new byte[1 + mustCount + picked.Count];
                buf[0] = (byte)(mustCount + picked.Count);
                for (int i = 0; i < mustCount; i++)
                    buf[1 + i] = 0;
                for (int i = 0; i < picked.Count; i++)
                    buf[1 + mustCount + i] = (byte)picked[i];
                SendResponseBytesLocked(buf);
                return (true, $"同调/仪式凑星选择：{string.Join(", ", picked.Select(i => opts[i].Name))}");
            });
    }

    private void ParseSelectPlaceLocked(BinaryReader packet, string promptType)
    {
        packet.ReadByte();
        int count = Math.Max(1, (int)packet.ReadByte());
        uint selectableMask = ~packet.ReadUInt32();

        List<AgentLegalActionChoice> choices = new();
        List<(byte RawPlayer, byte Loc, byte Seq, string Label)> slots = new();

        for (int zone = 0; zone < 32; zone++)
        {
            if ((selectableMask & (1u << zone)) == 0)
                continue;
            int seq = zone & 0xf;
            if (seq == 7 || seq > 13)
                continue;

            int sideOffset = zone >> 4;
            byte rawPlayer = (byte)GetLocalPlayer(sideOffset);
            byte loc = seq < 7 ? (byte)CardLocation.MonsterZone : (byte)CardLocation.SpellZone;
            byte realSeq = (byte)(seq < 7 ? seq : seq - 8);
            string zoneText = $"{(sideOffset == 0 ? "我方" : "对方")}.{(CardLocation)loc}[{realSeq}]";
            choices.Add(new AgentLegalActionChoice(choices.Count, "zone", zoneText, Zone: zoneText));
            slots.Add((rawPlayer, loc, realSeq, zoneText));
        }

        _selectHint = 0;

        SetPendingPromptLocked(
            promptType,
            $"请选择 {count} 个可用格子位置",
            canCancel: false,
            minSelect: count,
            maxSelect: count,
            supportsMulti: count > 1,
            choices,
            (idx, multi) =>
            {
                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count != count || picked.Any(x => x < 0 || x >= slots.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"请选择恰好 {count} 个有效格子下标（0 ~ {slots.Count - 1}）。");

                byte[] resp = new byte[count * 3];
                for (int i = 0; i < count; i++)
                {
                    var s = slots[picked[i]];
                    resp[i * 3] = s.RawPlayer;
                    resp[i * 3 + 1] = s.Loc;
                    resp[i * 3 + 2] = s.Seq;
                }
                SendResponseBytesLocked(resp);
                return (true, $"选择区域：{string.Join(", ", picked.Select(i => slots[i].Label))}");
            });
    }

    private void ParseSelectCounterLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int counterType = packet.ReadInt16();
        int quantity = packet.ReadInt16();
        int count = packet.ReadByte();

        List<(string Name, int MaxCounters)> cards = new(count);
        List<AgentLegalActionChoice> choices = new(count);
        for (int i = 0; i < count; i++)
        {
            int code = packet.ReadInt32();
            int con = GetLocalPlayer(packet.ReadByte());
            CardLocation loc = (CardLocation)packet.ReadByte();
            int seq = packet.ReadByte();
            int num = packet.ReadInt16();
            string name = LookupCardName(repo, code);
            cards.Add((name, num));
            choices.Add(new AgentLegalActionChoice(i, "counter_source", $"{name} ({(con == 0 ? "我方" : "对方")}.{loc}[{seq}], 持有={num})", code, name));
        }

        SetPendingPromptLocked(
            "select_counter",
            $"需取除 {quantity} 个指示物 (type=0x{counterType:X})，选择起始卡后将按顺序自动凑齐数量",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                int start = Math.Clamp(idx ?? 0, 0, Math.Max(0, cards.Count - 1));
                short[] allocated = new short[cards.Count];
                int rem = quantity;
                for (int step = 0; step < cards.Count && rem > 0; step++)
                {
                    int cIdx = (start + step) % cards.Count;
                    short take = (short)Math.Min(rem, cards[cIdx].MaxCounters);
                    allocated[cIdx] = take;
                    rem -= take;
                }

                byte[] buf = new byte[cards.Count * 2];
                for (int i = 0; i < cards.Count; i++)
                {
                    buf[i * 2] = (byte)(allocated[i] & 0xff);
                    buf[i * 2 + 1] = (byte)((allocated[i] >> 8) & 0xff);
                }
                SendResponseBytesLocked(buf);
                return (true, $"取除 {quantity} 个指示物");
            });
    }

    private void ParseSortCardLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        List<string> names = new(count);
        for (int i = 0; i < count; i++)
        {
            int code = packet.ReadInt32();
            packet.ReadBytes(3);
            names.Add(LookupCardName(repo, code));
        }

        SetPendingPromptLocked(
            "sort_card",
            $"排列卡片顺序：{string.Join(", ", names)}（选 0 保持原顺序，或通过 cardIndices 指定排列）",
            canCancel: false,
            minSelect: 1,
            maxSelect: count,
            supportsMulti: true,
            [new AgentLegalActionChoice(0, "keep_order", "保持默认顺序")],
            (_, multi) =>
            {
                if (multi is not null && multi.Count == count && multi.Distinct().Count() == count && multi.All(x => x >= 0 && x < count))
                {
                    byte[] custom = multi.Select(static x => (byte)x).ToArray();
                    SendResponseBytesLocked(custom);
                    return (true, "按自定义顺序排列卡片");
                }
                SendResponseInt32Locked(-1);
                return (true, "保持默认卡片顺序");
            });
    }

    private void ParseAnnounceAttribLocked(BinaryReader packet)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        int avail = packet.ReadInt32();
        List<AgentLegalActionChoice> choices = new();
        List<(int Mask, string Name)> items = new();
        for (int bit = 0; bit < 7; bit++)
        {
            int mask = 1 << bit;
            if ((avail & mask) != 0)
            {
                string label = ((CardAttribute)mask).ToString();
                choices.Add(new AgentLegalActionChoice(choices.Count, "attribute", label));
                items.Add((mask, label));
            }
        }

        SetPendingPromptLocked(
            "announce_attrib",
            $"请宣言 {count} 个属性",
            canCancel: false,
            minSelect: count,
            maxSelect: count,
            supportsMulti: count > 1,
            choices,
            (idx, multi) =>
            {
                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count != count || picked.Any(x => x < 0 || x >= items.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"请选择 {count} 个有效属性下标。");
                int sum = picked.Sum(i => items[i].Mask);
                SendResponseInt32Locked(sum);
                return (true, $"宣言属性：{string.Join(", ", picked.Select(i => items[i].Name))}");
            });
    }

    private void ParseAnnounceRaceLocked(BinaryReader packet)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        int avail = packet.ReadInt32();
        List<AgentLegalActionChoice> choices = new();
        List<(int Mask, string Name)> items = new();
        for (int bit = 0; bit < 26; bit++)
        {
            int mask = 1 << bit;
            if ((avail & mask) != 0)
            {
                string label = ((CardRace)mask).ToString();
                choices.Add(new AgentLegalActionChoice(choices.Count, "race", label));
                items.Add((mask, label));
            }
        }

        SetPendingPromptLocked(
            "announce_race",
            $"请宣言 {count} 个种族",
            canCancel: false,
            minSelect: count,
            maxSelect: count,
            supportsMulti: count > 1,
            choices,
            (idx, multi) =>
            {
                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count != count || picked.Any(x => x < 0 || x >= items.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"请选择 {count} 个有效种族下标。");
                int sum = picked.Sum(i => items[i].Mask);
                SendResponseInt32Locked(sum);
                return (true, $"宣言种族：{string.Join(", ", picked.Select(i => items[i].Name))}");
            });
    }

    private void ParseAnnounceNumberLocked(BinaryReader packet)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        List<int> nums = new(count);
        List<AgentLegalActionChoice> choices = new(count);
        for (int i = 0; i < count; i++)
        {
            int val = packet.ReadInt32();
            nums.Add(val);
            choices.Add(new AgentLegalActionChoice(i, "number", $"宣言数字：{val}"));
        }

        SetPendingPromptLocked(
            "announce_number",
            "请宣言一个数字",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, _) =>
            {
                if (idx is null || idx.Value < 0 || idx.Value >= nums.Count)
                    return (false, $"请选择 0 ~ {nums.Count - 1}。");
                SendResponseInt32Locked(idx.Value);
                return (true, $"宣言数字 {nums[idx.Value]}");
            });
    }

    private void ParseAnnounceCardLocked(BinaryReader packet, INamedCardRepository repo)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        for (int i = 0; i < count; i++)
            packet.ReadInt32();

        List<NamedCard> sample = repo.GetAllCards()
            .Where(static c => !c.HasType(CardType.Token))
            .Take(25)
            .ToList();

        List<AgentLegalActionChoice> choices = sample
            .Select((c, idx) => new AgentLegalActionChoice(idx, "announce_card", $"宣言卡名：{c.Name} (#{c.Id})", c.Id, c.Name))
            .ToList();
        if (choices.Count == 0)
            choices.Add(new AgentLegalActionChoice(0, "announce_card", "宣言卡密 #89631139", 89631139, "青眼白龙"));

        SetPendingPromptLocked(
            "announce_card",
            "请宣言一张卡名（可从候选选择 actionIndex，或在 cardIndices[0] 直接传入任意合法卡密 ID）",
            canCancel: false,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            choices,
            (idx, multi) =>
            {
                if (multi is { Count: > 0 } && multi[0] > 1000)
                {
                    SendResponseInt32Locked(multi[0]);
                    return (true, $"宣言卡密 #{multi[0]}");
                }
                if (idx is > 1000)
                {
                    SendResponseInt32Locked(idx.Value);
                    return (true, $"宣言卡密 #{idx.Value}");
                }
                if (idx is null || idx.Value < 0 || idx.Value >= choices.Count)
                    return (false, $"请选择 0 ~ {choices.Count - 1}，或在 cardIndices[0] 传入目标卡密。");
                int chosenCode = choices[idx.Value].CardCode ?? 89631139;
                SendResponseInt32Locked(chosenCode);
                return (true, $"宣言卡片：{choices[idx.Value].Label}");
            });
    }
}
