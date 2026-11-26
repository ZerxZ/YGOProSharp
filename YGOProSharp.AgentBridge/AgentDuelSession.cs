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

/// <summary>
/// 受限视角协议决斗代理会话：仅通过标准 YGOPro 客户端网络包（CTOS/STOC）参与或观战对局，
/// 严格执行 1v1 与 2v2 双打战争迷雾（绝不访问服务端内部状态、对手暗牌、非执子队友暗手牌或牌堆顺序）。
/// </summary>
public sealed partial class AgentDuelSession : IDisposable
{
    private const int EventLedgerHardCap = 10_000;
    private const int ChatHistoryCap = 32;

    private static readonly IReadOnlyDictionary<int, string> SystemStringMap = new Dictionary<int, string>
    {
        [30] = "场上怪兽数量变化（战斗卷回），是否继续攻击？",
        [31] = "对方场上没有怪兽，是否直接攻击对方玩家？",
        [90] = "是否不使用解放直接召唤？",
        [91] = "是否使用额外特殊召唤效果进行召唤？",
        [92] = "是否适用代替怪兽解放的效果？",
        [93] = "是否继续选择额外素材？",
        [94] = "是否发动此卡的效果？",
        [95] = "是否使用此卡的效果代替破坏/代价？",
        [96] = "是否将此卡的效果适用？",
        [98] = "是否把怪兽特殊召唤到连接区？",
        [200] = "是否从该区域发动卡片效果？",
        [210] = "是否继续选择卡片？",
        [218] = "是否以支付基本分代替？",
        [219] = "是否以取除指示物代替？",
        [220] = "是否以取除超量素材代替？",
        [221] = "是否在该时点发动诱发效果？",
        [500] = "请选择要送去墓地的卡",
        [501] = "请选择要丢弃的手牌",
        [502] = "请选择要破坏的卡",
        [503] = "请选择要除外的卡",
        [504] = "请选择要送去墓地的卡",
        [505] = "请选择要返回手牌的卡",
        [506] = "请选择要加入手牌（检索）的卡",
        [507] = "请选择要返回卡组的卡",
        [508] = "请选择要从卡组送去墓地的卡",
        [509] = "请选择要特殊召唤的怪兽",
        [510] = "请选择要翻开确认的卡",
        [511] = "请选择要作为对象的卡",
        [512] = "请选择要作为融合素材的怪兽",
        [513] = "请选择要作为同调素材的怪兽",
        [514] = "请选择要作为超量素材的怪兽",
        [515] = "请选择要盖放的卡",
        [516] = "请选择要改变表示形式的怪兽",
        [518] = "请选择要解放（祭品）的卡",
        [519] = "请选择要取除的超量素材",
        [520] = "请选择要无效效果的卡",
        [533] = "请选择要作为连接素材的怪兽",
        [569] = "请选择卡片放置的区域",
        [570] = "请选择要封锁的区域",
        [571] = "请选择要移动到的区域",
        [1150] = "发动卡片",
        [1151] = "通常召唤",
        [1152] = "特殊召唤",
        [1153] = "盖放卡片",
        [1154] = "反转召唤",
        [1155] = "守备表示",
        [1156] = "攻击表示",
        [1157] = "发动攻击",
        [1158] = "查看卡片",
        [1159] = "灵摆发动（设置灵摆刻度）",
        [1160] = "灵摆区域发动效果",
        [1161] = "适用持续效果",
        [1162] = "重置效果",
        [1163] = "灵摆召唤",
        [1164] = "连接召唤",
        [1165] = "超量召唤",
        [1166] = "同调召唤",
        [1167] = "融合召唤",
        [1168] = "仪式召唤"
    };

    private readonly ILogger _logger = AppLog.CreateLogger<AgentDuelSession>();
    private readonly object _sync = new();
    private readonly YGOClient _client;
    private readonly Func<INamedCardRepository> _cardRepoProvider;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly Task _pumpTask;
    private readonly short _clientVersion;
    private readonly bool _joinAsObserver;

    private TaskCompletionSource<bool> _turnSignal = CreateSignal();
    private TaskCompletionSource<bool> _pumpWakeSignal = CreateSignal();
    private bool _disposed;
    private bool _sentJoinHandshake;
    private bool _requestedObserverOnJoin;

    // 同步版本号与结算时间水位（用于消除观察快照滞后）
    private long _stateRevision;
    private DateTime _lastPacketReceivedUtc = DateTime.MinValue;

    // 大厅与基础房间状态
    private AgentSessionState _state = AgentSessionState.Connecting;
    private bool _isHost;
    private bool _isObserver;
    private bool _autoStartWhenAllReady;
    private int _seatIndex = -1;
    private int _roomMode;
    private int _masterRule = 5;
    private bool _isTag;
    private readonly string?[] _slotNames = new string?[4];
    private readonly bool[] _slotReady = new bool[4];

    // 对局状态（0 = 己方阵营，1 = 对方阵营）
    private bool _isFirst = true;
    private bool _chatOrderSwapped;
    private bool _activeTagDeckIsMine = true;
    private bool _teammateSurrenderPending;
    private int _turnCount;
    private DuelPhase _phase = DuelPhase.Draw;
    private int _currentTurnPlayer;
    private int _selectHint;
    private int? _remainingTimeSeconds;
    private string? _duelResult;
    private string? _lastError;

    private readonly SideTracker _self = new();
    private readonly SideTracker _oppo = new();
    private readonly List<AgentChainLinkView> _chain = new();

    // 只追加线性事件账本与增量观测游标
    private long _eventSeqCounter;
    private long _lastObservedEventSeq;
    private readonly List<(long Seq, string Text)> _eventLedger = new();
    private BoardDigest? _lastObservedBoard;

    // 结构化聊天队列与防注入隔离
    private long _chatSeqCounter;
    private readonly List<AgentChatEntry> _chatHistory = new();
    private DateTime _lastChatSentUtc = DateTime.MinValue;
    private string? _lastOutboundChatText;

    // 卡组无序画像、长期战略规划与 2v2 卡片力量战术黑板
    private List<AgentDeckCountSummary> _ownSubmittedDeckSummary = new();
    private YdkDeckData? _lastSubmittedDeck;
    private AgentPartnerTactic? _partnerTactic;
    private AgentStrategicPlan? _strategicPlan;

    // 快路径与重复连锁降噪策略
    private bool _autoConfirmSingleForced;
    private bool _suppressRepeatedChainPass;
    private int _silenceChainRemaining;
    private string? _echoedChainSignature;
    private int _echoedChainTurn = -1;
    private DuelPhase _echoedChainPhase = DuelPhase.Draw;

    // 当前待响应动作票据
    private long _nextPromptId;
    private PendingPromptInternal? _pendingPrompt;

    public AgentDuelSession(
        string sessionId,
        Guid ownerTokenId,
        string playerName,
        string roomName,
        string joinSpec,
        INetworkTransport transport,
        Func<INamedCardRepository> cardRepositoryProvider,
        uint clientVersion = 0x1362,
        bool joinAsObserver = false)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(cardRepositoryProvider);

        SessionId = sessionId;
        OwnerTokenId = ownerTokenId;
        PlayerName = string.IsNullOrWhiteSpace(playerName) ? "MCP_Duelist" : playerName.Trim();
        RoomName = roomName;
        JoinSpec = joinSpec;
        CreatedAtUtc = DateTime.UtcNow;
        _cardRepoProvider = cardRepositoryProvider;
        _clientVersion = unchecked((short)clientVersion);
        _joinAsObserver = joinAsObserver;

        _client = new YGOClient(transport);
        _client.Connected += HandleConnected;
        _client.PacketReceived += HandlePacketReceived;
        _client.Disconnected += HandleDisconnected;

        if (transport is InMemoryNetworkTransport inMem && !inMem.IsConnected)
            inMem.Connect();

        _pumpTask = Task.Run(() => RunPumpLoopAsync(_pumpCts.Token));
    }

    public string SessionId { get; }
    public Guid OwnerTokenId { get; }
    public string PlayerName { get; }
    public string RoomName { get; }
    public string JoinSpec { get; }
    public DateTime CreatedAtUtc { get; }

    public string? PersonaStyle { get; set; }

    public string? LastError
    {
        get
        {
            lock (_sync)
                return _lastError;
        }
    }

    public event Action<AgentThoughtNotification>? ThoughtEmitted;

    public bool IsConnected
    {
        get
        {
            lock (_sync)
                return !_disposed && _client.IsConnected && _state != AgentSessionState.Disconnected;
        }
    }

    public AgentSessionSummary CreateSummary()
    {
        lock (_sync)
        {
            return new AgentSessionSummary(
                SessionId,
                OwnerTokenId,
                PlayerName,
                RoomName,
                _state,
                !_disposed && _client.IsConnected && _state != AgentSessionState.Disconnected,
                _isObserver,
                _seatIndex,
                _pendingPrompt is not null,
                CreatedAtUtc);
        }
    }

    public AgentObservation GetObservation(AgentObservationDetail detail = AgentObservationDetail.Standard)
    {
        lock (_sync)
        {
            _client.Update();
            return BuildObservationLocked(detail == AgentObservationDetail.None ? AgentObservationDetail.Minimal : detail, advanceCursor: true);
        }
    }

    public IReadOnlyList<AgentDeckCountSummary> GetDeckList()
    {
        lock (_sync)
            return ComputeDeckSummaryWithRemainingLocked(_cardRepoProvider());
    }

    public AgentPendingPrompt? GetPendingPrompt()
    {
        lock (_sync)
        {
            _client.Update();
            return _pendingPrompt?.PublicPrompt;
        }
    }

    public void ConfigurePolicy(bool? autoConfirmSingleForced = null, bool? suppressRepeatedChainPass = null, int? silenceChainPrompts = null)
    {
        lock (_sync)
        {
            if (autoConfirmSingleForced.HasValue)
                _autoConfirmSingleForced = autoConfirmSingleForced.Value;
            if (suppressRepeatedChainPass.HasValue)
            {
                _suppressRepeatedChainPass = suppressRepeatedChainPass.Value;
                if (!_suppressRepeatedChainPass)
                    _echoedChainSignature = null;
            }
            if (silenceChainPrompts.HasValue)
                _silenceChainRemaining = Math.Clamp(silenceChainPrompts.Value, 0, 100);
        }
    }

    public AgentStrategicPlan UpdateStrategicPlan(
        string? winCondition,
        string? suspectedOpponentDeck,
        string? turnGoal,
        string? handoffToPartner,
        string? bo3SidePlan)
    {
        lock (_sync)
        {
            AgentStrategicPlan prev = _strategicPlan ?? new AgentStrategicPlan(null, null, null, null, null, DateTime.UtcNow);
            _strategicPlan = new AgentStrategicPlan(
                WinCondition: TrimNullable(winCondition, 200) ?? prev.WinCondition,
                SuspectedOpponentDeck: TrimNullable(suspectedOpponentDeck, 120) ?? prev.SuspectedOpponentDeck,
                TurnGoal: TrimNullable(turnGoal, 200) ?? prev.TurnGoal,
                HandoffToPartner: TrimNullable(handoffToPartner, 200) ?? prev.HandoffToPartner,
                Bo3SidePlan: TrimNullable(bo3SidePlan, 200) ?? prev.Bo3SidePlan,
                UpdatedAtUtc: DateTime.UtcNow);
            return _strategicPlan;
        }
    }

    public (bool Ok, string Message, AgentPartnerTactic? Tactic) SyncPartnerTactic(
        string? stance,
        string? note,
        bool sendToTeamChat = true,
        string? sharedBoardHandoffNote = null)
    {
        lock (_sync)
        {
            if (!_isTag)
                return (false, "当前房间不是 2v2 双打模式（Mode != 2）。", _partnerTactic);

            if (string.IsNullOrWhiteSpace(stance) &&
                string.IsNullOrWhiteSpace(note) &&
                string.IsNullOrWhiteSpace(sharedBoardHandoffNote))
            {
                return (true, _partnerTactic is null ? "当前暂无双打战术指令。" : "已读取当前双打战术方针。", _partnerTactic);
            }

            string normStance = string.IsNullOrWhiteSpace(stance)
                ? (_partnerTactic?.Stance ?? "balanced")
                : NormalizeStance(stance);
            string? cleanNote = TrimNullable(note, 140) ?? _partnerTactic?.Note;
            string? cleanHandoff = TrimNullable(sharedBoardHandoffNote, 180) ?? _partnerTactic?.SharedBoardHandoffNote;
            _partnerTactic = new AgentPartnerTactic(normStance, cleanNote, PlayerName, DateTime.UtcNow, cleanHandoff);

            if (sendToTeamChat && IsConnected && !_isObserver)
            {
                string combined = cleanNote ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(sharedBoardHandoffNote))
                {
                    string h = TrimNullable(sharedBoardHandoffNote, 140)!;
                    combined = string.IsNullOrEmpty(combined) ? $"[交接:{h}]" : $"{combined} [交接:{h}]";
                }
                string cmdText = string.IsNullOrWhiteSpace(combined)
                    ? $"/partner {normStance}"
                    : $"/partner {normStance} {combined}";
                SendChatLocked(cmdText);
            }

            return (true, $"已更新 2v2 搭档战术方针为 '{normStance}'。", _partnerTactic);
        }
    }

    public bool TryDeliverTeamPartnerDirective(int targetTeamId, string stance, string? note, string senderName)
    {
        lock (_sync)
        {
            if (!_isTag || _isObserver || _seatIndex is < 0 or >= 4)
                return false;
            int myTeam = _seatIndex >= 2 ? 1 : 0;
            if (myTeam != targetTeamId)
                return false;

            string normStance = NormalizeStance(stance);
            string? cleanNote = TrimNullable(note, 160);
            _partnerTactic = new AgentPartnerTactic(normStance, cleanNote, senderName, DateTime.UtcNow, _partnerTactic?.SharedBoardHandoffNote);
            AddEventLocked($"[2v2 搭档战术] 队友「{senderName}」指示方针【{normStance}】{(string.IsNullOrEmpty(cleanNote) ? "" : $"：{cleanNote}")}");
            PulseSignalLocked();
            return true;
        }
    }

    public async Task<AgentObservation> WaitForTurnAsync(
        TimeSpan timeout,
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        AgentObservationDetail effectiveDetail = detail == AgentObservationDetail.None ? AgentObservationDetail.Minimal : detail;
        DateTime deadline = DateTime.UtcNow + (timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(50) : timeout);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            Task waitTask;
            lock (_sync)
            {
                _client.Update();
                if (_pendingPrompt is not null ||
                    _teammateSurrenderPending ||
                    _state is AgentSessionState.DuelEnded or AgentSessionState.Disconnected)
                {
                    return BuildObservationLocked(effectiveDetail, advanceCursor: true);
                }

                waitTask = _turnSignal.Task;
            }

            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            using CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task delayTask = Task.Delay(remaining, delayCts.Token);
            Task finished = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);
            if (finished == waitTask)
                delayCts.Cancel();
        }

        lock (_sync)
        {
            _client.Update();
            return BuildObservationLocked(effectiveDetail, advanceCursor: true);
        }
    }

    public async Task<bool> WaitUntilJoinedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _client.Update();
                if (_state is AgentSessionState.Lobby or AgentSessionState.InDuel)
                    return true;
                if (_state == AgentSessionState.Disconnected)
                    return false;
            }
            await Task.Delay(8, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _client.Update();
            return _state is AgentSessionState.Lobby or AgentSessionState.InDuel;
        }
    }

    public (bool Ok, string Message) SubmitDeck(YdkDeckData deckData)
    {
        ArgumentNullException.ThrowIfNull(deckData);
        lock (_sync)
        {
            if (_isObserver)
                return (false, "观战身份无法提交卡组。");
            if (_state is not (AgentSessionState.Lobby or AgentSessionState.SideDecking))
                return (false, $"当前会话状态为 {_state}，仅可在大厅或换副卡组阶段提交卡组。");

            _lastError = null;
            SendDeckPacketLocked(deckData);
            _lastSubmittedDeck = deckData;
            _ownSubmittedDeckSummary = BuildUnorderedDeckSummary(deckData, _cardRepoProvider());

            if (_state == AgentSessionState.SideDecking && _pendingPrompt?.PublicPrompt.PromptType == "change_side")
                _pendingPrompt = null;

            WakePumpLocked();
            AddEventLocked($"已提交卡组（主卡组 {deckData.Main.Count} 张 / 额外 {deckData.Extra.Count} 张 / 副卡组 {deckData.Side.Count} 张）。");
            return (true, $"已提交卡组（主 {deckData.Main.Count} / 额外 {deckData.Extra.Count} / 副 {deckData.Side.Count}）。");
        }
    }

    public async Task<(bool Ok, string Message, AgentObservation Observation)> SetReadyAsync(
        bool ready,
        bool startIfHost = true,
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        long revBefore;
        lock (_sync)
        {
            if (_isObserver)
                return (false, "观战席无法切换准备状态。", BuildObservationLocked(detail, advanceCursor: false));
            if (_state != AgentSessionState.Lobby)
                return (false, $"当前状态为 {_state}，仅可在大厅阶段准备或取消准备。", BuildObservationLocked(detail, advanceCursor: false));

            _lastError = null;
            revBefore = _stateRevision;
            _autoStartWhenAllReady = ready && startIfHost;
            _client.Send(ready ? CtosMessage.HsReady : CtosMessage.HsNotReady);
            if (ready && startIfHost && _isHost && AreOtherSeatsReadyLocked())
                _client.Send(CtosMessage.HsStart);
            WakePumpLocked();
        }

        // 等待服务端实际校验卡组并回传 HsPlayerChange / ErrorMsg / 开局询问，杜绝准备后读取到旧快照或漏看卡组禁限错误
        DateTime settleDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(350);
        while (DateTime.UtcNow < settleDeadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _client.Update();
                if (_lastError is not null)
                {
                    string err = _lastError;
                    return (false, err, BuildObservationLocked(detail, advanceCursor: true));
                }

                bool seatMatched = _seatIndex is >= 0 and < 4 && _slotReady[_seatIndex] == ready;
                if (_state != AgentSessionState.Lobby || _pendingPrompt is not null || (seatMatched && (!startIfHost || !_isHost || !AreOtherSeatsReadyLocked())))
                    break;
                if (_stateRevision > revBefore && seatMatched && (DateTime.UtcNow - _lastPacketReceivedUtc).TotalMilliseconds >= 25)
                    break;
            }
            await Task.Delay(6, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _client.Update();
            if (_lastError is not null)
                return (false, _lastError, BuildObservationLocked(detail, advanceCursor: true));

            string msg = _state == AgentSessionState.InDuel || _pendingPrompt is not null
                ? "准备成功且对局已启动！"
                : (ready ? "已准备就绪。" : "已取消准备。");
            return (true, msg, BuildObservationLocked(detail, advanceCursor: true));
        }
    }

    public (bool Ok, string Message) SetReady(bool ready, bool startIfHost = false)
    {
        var (ok, msg, _) = SetReadyAsync(ready, startIfHost, AgentObservationDetail.Minimal).GetAwaiter().GetResult();
        return (ok, msg);
    }

    private bool AreOtherSeatsReadyLocked()
    {
        int total = _isTag ? 4 : 2;
        for (int i = 0; i < total; i++)
        {
            if (i == _seatIndex)
                continue;
            if (!_slotReady[i])
                return false;
        }
        return true;
    }

    public async Task<(bool Ok, string Message, AgentObservation Observation)> RequestStartDuelAsync(
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_isHost)
                return (false, "当前会话不是房主，无法发起开局。", BuildObservationLocked(detail, advanceCursor: false));
            if (_state != AgentSessionState.Lobby)
                return (false, "当前不在大厅等待阶段。", BuildObservationLocked(detail, advanceCursor: false));

            _lastError = null;
            _client.Send(CtosMessage.HsStart);
            WakePumpLocked();
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(350);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _client.Update();
                if (_state != AgentSessionState.Lobby || _pendingPrompt is not null || _lastError is not null)
                    break;
            }
            await Task.Delay(6, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _client.Update();
            return (true, "已作为房主发送开局请求。", BuildObservationLocked(detail, advanceCursor: true));
        }
    }

    public (bool Ok, string Message) RequestStartDuel()
    {
        var (ok, msg, _) = RequestStartDuelAsync(AgentObservationDetail.Minimal).GetAwaiter().GetResult();
        return (ok, msg);
    }

    public async Task<(bool Ok, string Message, AgentObservation Observation)> SwapSeatAsync(
        bool toObserver = false,
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        CancellationToken cancellationToken = default)
    {
        int prevSeat;
        bool prevObs;
        lock (_sync)
        {
            if (_state != AgentSessionState.Lobby)
                return (false, "仅可在大厅未开局阶段切换席位。", BuildObservationLocked(detail, advanceCursor: false));

            prevSeat = _seatIndex;
            prevObs = _isObserver;
            _client.Send(toObserver ? CtosMessage.HsToObserver : CtosMessage.HsToDuelist);
            WakePumpLocked();
        }

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _client.Update();
                if (_seatIndex != prevSeat || _isObserver != prevObs)
                    break;
            }
            await Task.Delay(6, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _client.Update();
            return (true, toObserver ? "已请求切换到观战席。" : $"当前席位 #{_seatIndex}。", BuildObservationLocked(detail, advanceCursor: true));
        }
    }

    public (bool Ok, string Message) SwapSeat(bool toObserver = false)
    {
        var (ok, msg, _) = SwapSeatAsync(toObserver, AgentObservationDetail.Minimal).GetAwaiter().GetResult();
        return (ok, msg);
    }

    public (bool Ok, string Message) SendChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, "聊天内容不能为空。");

        string trimmed = NormalizeChatText(message);
        if (trimmed.Length == 0)
            return (false, "聊天内容不能为空。");

        lock (_sync)
        {
            if (!IsConnected)
                return (false, "会话已断开连接。");

            DateTime now = DateTime.UtcNow;
            if ((now - _lastChatSentUtc).TotalMilliseconds < 600)
                return (false, "发言过快，请稍后再发送聊天。");

            _lastChatSentUtc = now;
            _lastOutboundChatText = trimmed;
            if (trimmed.StartsWith('/'))
                _lastError = null;
            SendChatLocked(trimmed);
            WakePumpLocked();
            return (true, "已发送房间消息。");
        }
    }

    public (bool Ok, string Message) Surrender()
    {
        lock (_sync)
        {
            if (_isObserver)
                return (false, "观战者不能投降。");
            if (_state != AgentSessionState.InDuel)
                return (false, "当前不在进行中的对局内。");

            _teammateSurrenderPending = false;
            _client.Send(CtosMessage.Surrender);
            WakePumpLocked();
            AddEventLocked(_isTag ? "已发送投降指令（2v2 模式下需队友同回合确认）。" : "已发送投降指令。");
            return (true, _isTag ? "已发送 2v2 投降请求（若队友已发起则立即生效，否则等待队友确认）。" : "已投降。");
        }
    }

    public AgentActionResult SubmitAction(
        int? actionIndex,
        IReadOnlyList<int>? cardIndices = null,
        long? expectedPromptId = null,
        string? thoughtSummary = null,
        string? chatMessage = null,
        AgentObservationDetail detail = AgentObservationDetail.Standard)
    {
        return SubmitActionAsync(actionIndex, cardIndices, expectedPromptId, thoughtSummary, chatMessage, detail, waitSettleMs: 750)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<AgentActionResult> SubmitActionAsync(
        int? actionIndex,
        IReadOnlyList<int>? indices = null,
        long? expectedPromptId = null,
        string? thoughtSummary = null,
        string? chatMessage = null,
        AgentObservationDetail detail = AgentObservationDetail.Standard,
        int waitSettleMs = 850,
        CancellationToken cancellationToken = default)
    {
        AgentThoughtNotification? thoughtToPublish = null;
        long handledId;
        string actionDesc;
        long revBefore;

        lock (_sync)
        {
            _client.Update();
            if (_pendingPrompt is null)
            {
                return BuildActionResultLocked(
                    false,
                    "当前没有等待您响应的动作询问，请调用 duel_wait_for_turn 等待轮到我方。",
                    null,
                    detail,
                    advanceCursor: false,
                    appliedSteps: 0,
                    stopReason: "no_open_prompt");
            }

            PendingPromptInternal current = _pendingPrompt;
            if (expectedPromptId.HasValue && expectedPromptId.Value != current.PublicPrompt.PromptId)
            {
                return BuildActionResultLocked(
                    false,
                    $"动作询问编号已过期（期望 {expectedPromptId.Value}，当前最新为 {current.PublicPrompt.PromptId}），未消耗当前询问，请按最新 choices 重新选择。",
                    null,
                    detail,
                    advanceCursor: true,
                    appliedSteps: 0,
                    stopReason: "stale_prompt");
            }

            // 统一动作参数语义：若为单选提示且调用方传了 indices/cardIndices=[x]，自动映射为 actionIndex=x
            int? unifiedSingle = actionIndex;
            if (!unifiedSingle.HasValue && indices is { Count: 1 })
                unifiedSingle = indices[0];

            (bool ok, string errOrDesc) = current.TryExecute(unifiedSingle, indices);
            if (!ok)
            {
                // 校验失败绝不消耗当前 _pendingPrompt 票据！
                return BuildActionResultLocked(
                    false,
                    errOrDesc,
                    null,
                    detail,
                    advanceCursor: false,
                    appliedSteps: 0,
                    stopReason: "invalid_choice");
            }

            // 记录同阶段重复可取消连锁的 Pass 指纹（供 suppressRepeatedChainPass 自动折叠）
            if (_suppressRepeatedChainPass &&
                current.PublicPrompt.PromptType == "select_chain" &&
                current.PublicPrompt.CanCancel &&
                unifiedSingle == 0)
            {
                _echoedChainSignature = current.ChainSignature;
                _echoedChainTurn = _turnCount;
                _echoedChainPhase = _phase;
            }
            else if (current.PublicPrompt.PromptType != "select_chain" || unifiedSingle != 0)
            {
                _echoedChainSignature = null;
            }

            handledId = current.PublicPrompt.PromptId;
            actionDesc = errOrDesc;
            _lastError = null;
            revBefore = _stateRevision;
            if (ReferenceEquals(_pendingPrompt, current))
                _pendingPrompt = null;

            AddEventLocked($"[我方决策 #{handledId}] {errOrDesc}");

            if (!string.IsNullOrWhiteSpace(thoughtSummary))
            {
                string cleanThought = thoughtSummary.Trim();
                if (cleanThought.Length > 240)
                    cleanThought = cleanThought[..240];
                thoughtToPublish = new AgentThoughtNotification(
                    OwnerTokenId,
                    SessionId,
                    PlayerName,
                    RoomName,
                    _turnCount,
                    _phase.ToString(),
                    cleanThought,
                    DateTime.UtcNow);
            }

            if (!string.IsNullOrWhiteSpace(chatMessage))
            {
                string cleanChat = NormalizeChatText(chatMessage);
                DateTime now = DateTime.UtcNow;
                bool isDuplicateRecent = string.Equals(cleanChat, _lastOutboundChatText, StringComparison.Ordinal) &&
                                         (now - _lastChatSentUtc).TotalSeconds < 12;
                if (cleanChat.Length > 0 && !isDuplicateRecent && (now - _lastChatSentUtc).TotalMilliseconds >= 1500)
                {
                    _lastChatSentUtc = now;
                    _lastOutboundChatText = cleanChat;
                    SendChatLocked(cleanChat);
                }
            }

            WakePumpLocked();
        }

        if (thoughtToPublish is not null)
        {
            try
            {
                ThoughtEmitted?.Invoke(thoughtToPublish);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Thought notification listener failed.");
            }
        }

        // 等待服务端 ocgcore 完成本次动作结算并推送后续场面移动或下一条待响应提示，彻底消除「召唤后怪兽区仍为空」滞后问题
        await WaitPostActionSettleAsync(revBefore, Math.Clamp(waitSettleMs, 20, 4000), cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _client.Update();
            return BuildActionResultLocked(
                true,
                $"已提交动作：{actionDesc}",
                handledId,
                detail,
                advanceCursor: true,
                appliedSteps: 1,
                stopReason: null);
        }
    }
}
