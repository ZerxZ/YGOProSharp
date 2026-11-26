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
    private AgentActionResult BuildActionResultLocked(
        bool success,
        string message,
        long? handledPromptId,
        AgentObservationDetail detail,
        bool advanceCursor,
        int appliedSteps,
        string? stopReason)
    {
        if (detail == AgentObservationDetail.None)
        {
            string effectiveMessage = message;
            if (!string.IsNullOrWhiteSpace(_lastError) && !effectiveMessage.Contains(_lastError, StringComparison.Ordinal))
                effectiveMessage = $"{effectiveMessage}（{_lastError}）";
            if (advanceCursor)
                _lastError = null;

            (IReadOnlyList<string>? newEvts, IReadOnlyList<string>? changes) = ComputeDeltaLocked(advanceCursor);
            return new AgentActionResult(
                Success: success,
                Message: effectiveMessage,
                HandledPromptId: handledPromptId,
                State: _state,
                DuelResult: _duelResult,
                HasPendingAction: _pendingPrompt is not null,
                NextPrompt: _pendingPrompt?.PublicPrompt,
                NewEvents: newEvts,
                BoardChanges: changes,
                Observation: null,
                AppliedSteps: appliedSteps,
                StopReason: stopReason);
        }

        AgentObservation obs = BuildObservationLocked(detail, advanceCursor);
        return new AgentActionResult(
            Success: success,
            Message: message,
            HandledPromptId: handledPromptId,
            State: _state,
            DuelResult: _duelResult,
            HasPendingAction: obs.HasPendingAction,
            NextPrompt: obs.PendingPrompt,
            NewEvents: obs.NewEvents,
            BoardChanges: obs.BoardChanges,
            Observation: obs,
            AppliedSteps: appliedSteps,
            StopReason: stopReason);
    }

    private AgentObservation BuildObservationLocked(AgentObservationDetail detail, bool advanceCursor)
    {
        INamedCardRepository repo = _cardRepoProvider();
        int maxSlots = _isTag ? 4 : 2;
        List<AgentLobbySlotView> lobbySlots = new(maxSlots);
        for (int i = 0; i < maxSlots; i++)
        {
            lobbySlots.Add(new AgentLobbySlotView(
                i,
                _isTag ? (i >= 2 ? 1 : 0) : i,
                _slotNames[i],
                _slotReady[i]));
        }

        string? teammate = null;
        AgentTagTeamContext? tagCtx = null;
        if (_isTag && _seatIndex is >= 0 and < 4)
        {
            int partnerSeat = _seatIndex ^ 1;
            int myTeam = _seatIndex / 2;
            int enemyBase = myTeam == 0 ? 2 : 0;
            teammate = _slotNames[partnerSeat];
            string[] enemies =
            [
                _slotNames[enemyBase] ?? $"席位#{enemyBase}",
                _slotNames[enemyBase + 1] ?? $"席位#{enemyBase + 1}"
            ];

            string turnRel = _currentTurnPlayer == 0
                ? (_activeTagDeckIsMine ? "self" : "teammate")
                : "enemy";
            string nextHint = _currentTurnPlayer == 0
                ? "我方回合结束后将交由敌方阵营行动，你的场上怪兽/盖卡与墓地会共享留给下一轮我方搭档。"
                : (!_activeTagDeckIsMine
                    ? "当前敌方回合结束后，将立即轮到你本人执子抽卡并接管我方共享场面！"
                    : $"当前敌方回合结束后，将轮到你的队友「{teammate ?? "搭档"}」执子行动。");

            int[] seatOrder = _chatOrderSwapped ? [2, 0, 3, 1] : [0, 2, 1, 3];
            AgentTagRotationSlot[] rotation = new AgentTagRotationSlot[4];
            for (int r = 0; r < 4; r++)
            {
                int sIdx = seatOrder[r];
                string rel = sIdx == _seatIndex
                    ? "self"
                    : ((sIdx / 2) == myTeam ? "teammate" : "enemy");
                rotation[r] = new AgentTagRotationSlot(r + 1, sIdx, _slotNames[sIdx] ?? $"席位#{sIdx}", rel);
            }

            tagCtx = new AgentTagTeamContext(
                MySeatIndex: _seatIndex,
                MyTeamId: myTeam,
                PartnerSeatIndex: partnerSeat,
                PartnerName: teammate,
                EnemyNames: enemies,
                CurrentTurnRelation: turnRel,
                NextTurnHint: nextHint,
                TurnRotationOrder: rotation,
                SharedBoardHandoffNote: _partnerTactic?.SharedBoardHandoffNote ?? _strategicPlan?.HandoffToPartner);
        }

        (IReadOnlyList<string>? newEvents, IReadOnlyList<string>? boardChanges) = ComputeDeltaLocked(advanceCursor);

        int recentCap = detail == AgentObservationDetail.Full ? 50 : (detail == AgentObservationDetail.Standard ? 14 : 6);
        string[] recentSlice = _eventLedger.Count == 0
            ? []
            : _eventLedger.Skip(Math.Max(0, _eventLedger.Count - recentCap)).Select(static e => e.Text).ToArray();

        IReadOnlyList<AgentDeckCountSummary>? deckList = detail == AgentObservationDetail.Full && _ownSubmittedDeckSummary.Count > 0
            ? ComputeDeckSummaryWithRemainingLocked(repo)
            : null;

        IReadOnlyList<AgentChatEntry>? chats = _chatHistory.Count > 0
            ? _chatHistory.Skip(Math.Max(0, _chatHistory.Count - (detail == AgentObservationDetail.Minimal ? 4 : 10))).ToArray()
            : null;

        string? lastErrorSnapshot = _lastError;
        if (advanceCursor)
            _lastError = null;

        return new AgentObservation(
            SessionId: SessionId,
            PlayerName: PlayerName,
            RoomName: RoomName,
            JoinSpec: JoinSpec,
            State: _state,
            IsConnected: !_disposed && _client.IsConnected && _state != AgentSessionState.Disconnected,
            IsObserver: _isObserver,
            IsHost: _isHost,
            SeatIndex: _seatIndex,
            IsTag: _isTag,
            RoomMode: _roomMode,
            MasterRule: _masterRule,
            TeammateName: teammate,
            ActiveTagDeckIsMine: _activeTagDeckIsMine,
            TeammateSurrenderPending: _teammateSurrenderPending,
            TurnCount: _turnCount,
            CurrentPhase: _phase.ToString(),
            CurrentTurnOwner: _currentTurnPlayer == 0 ? "self" : "opponent",
            RemainingTimeSeconds: _remainingTimeSeconds,
            DuelResult: _duelResult,
            LastError: lastErrorSnapshot,
            LobbySlots: lobbySlots,
            SelfSide: _self.BuildView(repo, detail),
            OpponentSide: _oppo.BuildView(repo, detail),
            CurrentChain: _chain.ToArray(),
            OwnSubmittedDeckList: deckList,
            LastEventSeq: _eventSeqCounter,
            NewEvents: newEvents,
            BoardChanges: boardChanges,
            RecentEvents: recentSlice,
            RecentChats: chats,
            ChatTrustNotice: chats is not null ? "untrusted_player_chat" : null,
            TagContext: tagCtx,
            PartnerTactic: _partnerTactic,
            StrategicPlan: _strategicPlan,
            HasPendingAction: _pendingPrompt is not null,
            PendingPrompt: _pendingPrompt?.PublicPrompt);
    }

    private (IReadOnlyList<string>? NewEvents, IReadOnlyList<string>? BoardChanges) ComputeDeltaLocked(bool advanceCursor)
    {
        List<string> deltaEvents = new();
        for (int i = _eventLedger.Count - 1; i >= 0; i--)
        {
            if (_eventLedger[i].Seq <= _lastObservedEventSeq)
                break;
            deltaEvents.Add(_eventLedger[i].Text);
        }
        deltaEvents.Reverse();
        if (deltaEvents.Count > 30)
            deltaEvents = [$"...(省略中间 {deltaEvents.Count - 28} 条)...", .. deltaEvents[^28..]];

        INamedCardRepository repo = _cardRepoProvider();
        BoardDigest nowBoard = CaptureBoardDigestLocked(repo);
        List<string>? diff = null;
        if (_lastObservedBoard is BoardDigest prev)
        {
            diff = new List<string>();
            if (prev.Turn != nowBoard.Turn || prev.Phase != nowBoard.Phase)
                diff.Add($"回合/阶段: T{prev.Turn} {prev.Phase} -> T{nowBoard.Turn} {nowBoard.Phase}");
            if (prev.SelfLp != nowBoard.SelfLp || prev.OppoLp != nowBoard.OppoLp)
                diff.Add($"LP: 我方 {prev.SelfLp}->{nowBoard.SelfLp} | 对方 {prev.OppoLp}->{nowBoard.OppoLp}");
            if (prev.SelfMzone != nowBoard.SelfMzone)
                diff.Add($"我方怪兽区: [{prev.SelfMzone}] -> [{nowBoard.SelfMzone}]");
            if (prev.OppoMzone != nowBoard.OppoMzone)
                diff.Add($"对方怪兽区: [{prev.OppoMzone}] -> [{nowBoard.OppoMzone}]");
            if (prev.SelfSzoneCount != nowBoard.SelfSzoneCount || prev.OppoSzoneCount != nowBoard.OppoSzoneCount)
                diff.Add($"魔陷数: 我方 {prev.SelfSzoneCount}->{nowBoard.SelfSzoneCount}, 对方 {prev.OppoSzoneCount}->{nowBoard.OppoSzoneCount}");
            if (prev.SelfHandCount != nowBoard.SelfHandCount || prev.OppoHandCount != nowBoard.OppoHandCount)
                diff.Add($"手牌数: 我方 {prev.SelfHandCount}->{nowBoard.SelfHandCount}, 对方 {prev.OppoHandCount}->{nowBoard.OppoHandCount}");
            if (diff.Count == 0)
                diff = null;
        }

        if (advanceCursor)
        {
            _lastObservedEventSeq = _eventSeqCounter;
            _lastObservedBoard = nowBoard;
        }

        return (deltaEvents.Count > 0 ? deltaEvents : null, diff);
    }

    private BoardDigest CaptureBoardDigestLocked(INamedCardRepository repo)
    {
        string FormatMzone(SideTracker s) =>
            string.Join(", ", s.MonsterZone
                .Select((m, idx) => m is null ? null : (m.Code > 0 ? $"{LookupCardName(repo, m.Code)}@{idx}" : $"盖怪@{idx}"))
                .Where(static x => x is not null));

        return new BoardDigest(
            _turnCount,
            _phase,
            _self.LifePoints,
            _oppo.LifePoints,
            _self.Hand.Count,
            _oppo.Hand.Count,
            _self.SpellZone.Count(static x => x is not null),
            _oppo.SpellZone.Count(static x => x is not null),
            FormatMzone(_self),
            FormatMzone(_oppo));
    }

    private void SetPendingPromptLocked(
        string promptType,
        string hintText,
        bool canCancel,
        int minSelect,
        int maxSelect,
        bool supportsMulti,
        IReadOnlyList<AgentLegalActionChoice> choices,
        Func<int?, IReadOnlyList<int>?, (bool Ok, string Message)> executor,
        string? chainSignature = null,
        bool allowAutoForced = true)
    {
        // [fast] 唯一合法选项自动确认（严守 choices == 1 && min <= 1 && max <= 1，不误触多格选择或换副卡组）
        if (allowAutoForced && _autoConfirmSingleForced && choices.Count == 1 && minSelect <= 1 && maxSelect <= 1)
        {
            (bool fastOk, string fastDesc) = executor(0, null);
            if (fastOk)
            {
                AddEventLocked($"[fast] 唯一选项自动执行 ({promptType})：{fastDesc}");
                return;
            }
        }

        long id = ++_nextPromptId;
        string submitField = supportsMulti || minSelect > 1 ? "cardIndices" : "actionIndex";
        AgentPendingPrompt pub = new(id, promptType, hintText, canCancel, minSelect, maxSelect, supportsMulti, submitField, choices);
        _pendingPrompt = new PendingPromptInternal(pub, executor, chainSignature);
        PulseSignalLocked();
    }

    private IReadOnlyList<AgentDeckCountSummary> ComputeDeckSummaryWithRemainingLocked(INamedCardRepository repo)
    {
        if (_ownSubmittedDeckSummary.Count == 0)
            return [];

        // 基于己方可见区域（手牌、场上、墓地、表侧除外、已召唤额外）扣减计算无序剩余数量，绝不窥探牌堆顺序
        Dictionary<int, int> knownOutsideDeck = new();
        void CountCard(int code)
        {
            if (code <= 0) return;
            knownOutsideDeck[code] = knownOutsideDeck.GetValueOrDefault(code, 0) + 1;
        }

        foreach (TrackedCard c in _self.Hand) CountCard(c.Code);
        foreach (TrackedCard? c in _self.MonsterZone)
        {
            if (c is null) continue;
            CountCard(c.Code);
            foreach (int ov in c.Overlays) CountCard(ov);
        }
        foreach (TrackedCard? c in _self.SpellZone)
        {
            if (c is not null) CountCard(c.Code);
        }
        foreach (TrackedCard c in _self.Grave) CountCard(c.Code);
        foreach (TrackedCard c in _self.Banished) CountCard(c.Code);

        List<AgentDeckCountSummary> updated = new(_ownSubmittedDeckSummary.Count);
        foreach (AgentDeckCountSummary item in _ownSubmittedDeckSummary)
        {
            int outCount = item.Section is "main" or "extra"
                ? knownOutsideDeck.GetValueOrDefault(item.CardCode, 0)
                : 0;
            int rem = item.Section == "side" ? item.Copies : Math.Max(0, item.Copies - outCount);
            updated.Add(item with { RemainingInDeck = rem });
        }

        return updated;
    }

    private static string DecodeServerErrorMessage(int errType, int errCode, INamedCardRepository repo)
    {
        switch (errType)
        {
            case 1: // JoinError
                return errCode switch
                {
                    1 => "加入房间被拒：客户端协议版本号不匹配。",
                    2 => "加入房间被拒：房间密码错误。",
                    _ => "加入房间被拒：房间已满、已开局或不允许加入。"
                };

            case 2: // DeckError
            {
                if (errCode == 1)
                    return "卡组未通过检查：主卡组张数需在 40~60 张之间，额外与副卡组各不超过 15 张。";

                int highFlag = (errCode >> 28) & 0xF;
                int cardId = highFlag != 0 ? (errCode & 0x0FFFFFFF) : errCode;
                string reason = highFlag switch
                {
                    1 => "超出房间当前禁限卡表允许投入上限（禁止卡或限1/准限超标）",
                    2 => "不属于本房间允许的 OCG 卡池",
                    3 => "不属于本房间允许的 TCG 卡池",
                    4 => "未知卡片 ID",
                    5 => "主/额外/副卡组张数不符合规则",
                    _ => "违反本房间禁限卡表张数限制或 OCG/TCG/DIY 卡池范围"
                };

                if (cardId > 10 && repo.TryGetCard(cardId, out NamedCard? nc) && nc is not null)
                {
                    return $"准备失败（卡组不合法）：违规卡片「{nc.Name}」(#{cardId}) — {reason}。建议重新调用 duel_set_deck 生成适配本房间的合法卡组，或使用 NF#/NC# 无禁限房间。";
                }

                return $"准备失败（卡组不合法）：违规卡密 #{cardId} (0x{errCode:X}) — {reason}。建议重新调用 duel_set_deck 生成适配本房间的合法卡组，或使用 NF#/NC# 无禁限房间。";
            }

            case 3: // SideError
                return "更换副卡组失败：换副前后的主卡组、额外卡组与副卡组张数必须保持完全一致。";

            case 4: // VersionError
                return $"协议版本不匹配 (0x{errCode:X})。";

            default:
                return $"服务端返回提示：type={errType}, code=0x{errCode:X}";
        }
    }

    private static string? ResolveEffectDescription(INamedCardRepository repo, int desc, int fallbackCardCode)
    {
        if (desc == 0)
            return null;

        if (SystemStringMap.TryGetValue(desc, out string? sysText))
            return sysText;

        // 标准 ocgcore aux.Stringid(code, offset) = (code << 4) + offset (offset 0..15 对应 texts.str1..str16)
        uint uDesc = unchecked((uint)desc);
        if (uDesc >= 160)
        {
            int strCardId = (int)(uDesc >> 4);
            int strOffset = (int)(uDesc & 0xF);
            if (strCardId > 0 && repo.TryGetCard(strCardId, out NamedCard? nc) && nc is not null)
            {
                string? subStr = nc.GetEffectString(strOffset);
                if (!string.IsNullOrWhiteSpace(subStr))
                    return $"「{nc.Name}」：{subStr}";

                string brief = nc.Description;
                if (brief.Length > 80)
                    brief = brief[..80] + "...";
                return $"「{nc.Name}」效果#{strOffset + 1}（{brief}）";
            }
        }

        if (fallbackCardCode > 0 && repo.TryGetCard(fallbackCardCode, out NamedCard? fb) && fb is not null)
        {
            string brief = fb.Description;
            if (brief.Length > 80)
                brief = brief[..80] + "...";
            return brief;
        }

        return null;
    }

    private void SendDeckPacketLocked(YdkDeckData deck)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)CtosMessage.UpdateDeck);
        writer.Write(deck.Main.Count + deck.Extra.Count);
        writer.Write(deck.Side.Count);
        foreach (int id in deck.Main)
            writer.Write(id);
        foreach (int id in deck.Extra)
            writer.Write(id);
        foreach (int id in deck.Side)
            writer.Write(id);
        writer.Flush();
        _client.Send(ms.ToArray());
    }

    private void SendChatLocked(string text)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)CtosMessage.Chat);
        writer.WriteUnicodeAutoLength(text, 255);
        writer.Flush();
        _client.Send(ms.ToArray());
    }

    private void SendResponseInt32Locked(int value)
    {
        _client.Send(CtosMessage.Response, value);
    }

    private void SendResponseBytesLocked(byte[] payload)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)CtosMessage.Response);
        writer.Write(payload);
        writer.Flush();
        _client.Send(ms.ToArray());
    }

    private int GetLocalPlayer(int serverPlayer)
    {
        if (serverPlayer is not (0 or 1))
            return serverPlayer;
        return _isFirst ? serverPlayer : 1 - serverPlayer;
    }

    private SideTracker GetSide(int localPlayer) => localPlayer == 0 ? _self : _oppo;

    private (string Name, string Relation, int? Seat) ResolveChatSpeakerInfo(int senderCode)
    {
        if (senderCode < 4)
        {
            int pos = senderCode;
            if (_chatOrderSwapped)
                pos = _isTag ? senderCode ^ 2 : 1 - senderCode;

            string name = _slotNames[pos] ?? $"Player#{pos}";
            if (_seatIndex < 0)
                return (name, "player", pos);
            if (pos == _seatIndex)
                return (name, "self", pos);
            if (_isTag && (pos / 2) == (_seatIndex / 2))
                return (name, "teammate", pos);
            return (name, "enemy", pos);
        }

        if (senderCode == 8 || senderCode is >= 11 and <= 19)
            return ("System", "system", null);

        return ("Observer", "observer", null);
    }

    private void AddEventLocked(string text)
    {
        long seq = ++_eventSeqCounter;
        if (_eventLedger.Count >= EventLedgerHardCap)
            _eventLedger.RemoveRange(0, 1000);
        _eventLedger.Add((seq, text));
    }

    private void PulseSignalLocked()
    {
        TaskCompletionSource<bool> prev = _turnSignal;
        _turnSignal = CreateSignal();
        prev.TrySetResult(true);
    }

    private void WakePumpLocked()
    {
        TaskCompletionSource<bool> prev = _pumpWakeSignal;
        _pumpWakeSignal = CreateSignal();
        prev.TrySetResult(true);
    }

    private static TaskCompletionSource<bool> CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static List<int> ResolveSelectedIndices(int? single, IReadOnlyList<int>? multi)
    {
        if (multi is { Count: > 0 })
            return multi.ToList();
        if (single.HasValue)
            return [single.Value];
        return [];
    }

    private static string NormalizeChatText(string raw)
    {
        string cleaned = string.Join(' ', raw.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return cleaned.Length <= 220 ? cleaned : cleaned[..220];
    }

    private static string NormalizeStance(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "balanced";
        return raw.Trim().ToLowerInvariant() switch
        {
            "agg" or "aggressive" or "激进" => "aggressive",
            "otk" or "all_in_otk" or "斩杀" => "all_in_otk",
            "ctrl" or "control" or "稳健" => "control",
            "setup" or "setup_partner" or "铺垫" => "setup_partner",
            "def" or "defensive" or "防守" => "defensive",
            _ => "balanced"
        };
    }

    private static string? TrimNullable(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string t = value.Trim();
        return t.Length <= maxLen ? t : t[..maxLen];
    }

    private static string LookupCardName(INamedCardRepository repo, int code)
    {
        if (code <= 0)
            return "未知暗牌";
        return repo.TryGetCard(code, out NamedCard? nc) && nc is not null && !string.IsNullOrWhiteSpace(nc.Name)
            ? nc.Name
            : $"#{code}";
    }

    private static (string Name, int? Atk, int? Def, string? ShortEffect) LookupCardBrief(INamedCardRepository repo, int code, TrackedCard? tracked)
    {
        if (code <= 0)
            return ("未知暗牌", null, null, null);

        if (repo.TryGetCard(code, out NamedCard? nc) && nc is not null)
        {
            string? eff = nc.Description;
            if (eff is { Length: > 110 })
                eff = eff[..110] + "...";
            int? atk = tracked?.Attack ?? ((nc.Type & (int)CardType.Monster) != 0 ? nc.Attack : null);
            int? def = tracked?.Defense ?? ((nc.Type & (int)CardType.Monster) != 0 ? nc.Defense : null);
            return (string.IsNullOrWhiteSpace(nc.Name) ? $"#{code}" : nc.Name, atk, def, eff);
        }

        return ($"#{code}", tracked?.Attack, tracked?.Defense, null);
    }

    private static List<AgentDeckCountSummary> BuildUnorderedDeckSummary(YdkDeckData deck, INamedCardRepository repo)
    {
        List<AgentDeckCountSummary> list = new();
        AppendSection(deck.Main, "main");
        AppendSection(deck.Extra, "extra");
        AppendSection(deck.Side, "side");
        return list;

        void AppendSection(IReadOnlyList<int> ids, string section)
        {
            foreach (IGrouping<int, int> g in ids.GroupBy(static id => id).OrderBy(static g => g.Key))
            {
                string? name = repo.TryGetCard(g.Key, out NamedCard? nc) ? nc?.Name : null;
                int cnt = g.Count();
                list.Add(new AgentDeckCountSummary(g.Key, name, cnt, cnt, section));
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _state = AgentSessionState.Disconnected;
            _pendingPrompt = null;
            PulseSignalLocked();
            WakePumpLocked();
        }

        _pumpCts.Cancel();
        try
        {
            _client.Connected -= HandleConnected;
            _client.PacketReceived -= HandlePacketReceived;
            _client.Disconnected -= HandleDisconnected;
            _client.Close();
        }
        catch
        {
        }
        _pumpCts.Dispose();
    }

    private readonly record struct BoardDigest(
        int Turn,
        DuelPhase Phase,
        int SelfLp,
        int OppoLp,
        int SelfHandCount,
        int OppoHandCount,
        int SelfSzoneCount,
        int OppoSzoneCount,
        string SelfMzone,
        string OppoMzone);

    private sealed class PendingPromptInternal(
        AgentPendingPrompt publicPrompt,
        Func<int?, IReadOnlyList<int>?, (bool Ok, string Message)> handler,
        string? chainSignature = null)
    {
        public AgentPendingPrompt PublicPrompt { get; } = publicPrompt;
        public string? ChainSignature { get; } = chainSignature;
        public (bool Ok, string Message) TryExecute(int? idx, IReadOnlyList<int>? multi) => handler(idx, multi);
    }
}
