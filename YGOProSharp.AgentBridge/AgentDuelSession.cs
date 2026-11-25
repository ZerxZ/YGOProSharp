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
public sealed class AgentDuelSession : IDisposable
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

    public (bool Ok, string Message, AgentPartnerTactic? Tactic) SyncPartnerTactic(string? stance, string? note, bool sendToTeamChat = true)
    {
        lock (_sync)
        {
            if (!_isTag)
                return (false, "当前房间不是 2v2 双打模式（Mode != 2）。", _partnerTactic);

            if (string.IsNullOrWhiteSpace(stance) && string.IsNullOrWhiteSpace(note))
                return (true, _partnerTactic is null ? "当前暂无双打战术指令。" : "已读取当前双打战术方针。", _partnerTactic);

            string normStance = NormalizeStance(stance);
            string? cleanNote = TrimNullable(note, 140);
            _partnerTactic = new AgentPartnerTactic(normStance, cleanNote, PlayerName, DateTime.UtcNow);

            if (sendToTeamChat && IsConnected && !_isObserver)
            {
                string cmdText = string.IsNullOrWhiteSpace(cleanNote)
                    ? $"/partner {normStance}"
                    : $"/partner {normStance} {cleanNote}";
                SendChatLocked(cmdText);
            }

            return (true, $"已更新 2v2 搭档战术方针为 '{normStance}'。", _partnerTactic);
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

    private async Task RunPumpLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                Task wakeTask;
                lock (_sync)
                {
                    if (!_sentJoinHandshake && _client.IsConnected)
                        SendInitialHandshakeLocked();

                    _client.Update();
                    wakeTask = _pumpWakeSignal.Task;
                }

                using CancellationTokenSource delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task finished = await Task.WhenAny(wakeTask, Task.Delay(4, delayCts.Token)).ConfigureAwait(false);
                if (finished == wakeTask)
                    delayCts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AgentDuelSession pump loop faulted for Session={SessionId}.", SessionId);
            lock (_sync)
            {
                _lastError = ex.Message;
                _state = AgentSessionState.Disconnected;
                PulseSignalLocked();
            }
        }
    }

    private void HandleConnected()
    {
        lock (_sync)
        {
            if (!_sentJoinHandshake)
                SendInitialHandshakeLocked();
        }
    }

    private void SendInitialHandshakeLocked()
    {
        _sentJoinHandshake = true;

        using (MemoryStream ms = new())
        using (BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.PlayerInfo);
            writer.WriteUnicode(PlayerName, 20);
            writer.Flush();
            _client.Send(ms.ToArray());
        }

        using (MemoryStream ms = new())
        using (BinaryWriter writer = new(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.JoinGame);
            writer.Write(_clientVersion);
            writer.Write(new byte[] { 0xCC, 0xCC, 0x00, 0x00, 0x00, 0x00 });
            writer.WriteUnicode(JoinSpec, 20);
            writer.Flush();
            _client.Send(ms.ToArray());
        }
    }

    private void HandleDisconnected(Exception? ex)
    {
        lock (_sync)
        {
            if (_state != AgentSessionState.DuelEnded)
                _state = AgentSessionState.Disconnected;
            if (ex is not null)
                _lastError = ex.Message;
            AddEventLocked("与房间的连接已断开。");
            PulseSignalLocked();
        }
    }

    private void HandlePacketReceived(BinaryReader packet)
    {
        try
        {
            _stateRevision++;
            _lastPacketReceivedUtc = DateTime.UtcNow;

            StocMessage id = (StocMessage)packet.ReadByte();
            if (id == StocMessage.GameMsg)
            {
                GameMessage gm = (GameMessage)packet.ReadByte();
                HandleGameMessageLocked(gm, packet);
                return;
            }

            switch (id)
            {
                case StocMessage.JoinGame:
                    packet.ReadUInt32(); // lflist
                    packet.ReadByte(); // rule/region
                    _roomMode = packet.ReadByte();
                    _masterRule = packet.ReadByte();
                    _isTag = _roomMode == 2;
                    _state = AgentSessionState.Lobby;
                    _lastError = null;
                    AddEventLocked($"成功加入房间 {RoomName}（Mode={_roomMode}, MR{_masterRule}）。");
                    if (_joinAsObserver && !_requestedObserverOnJoin)
                    {
                        _requestedObserverOnJoin = true;
                        _client.Send(CtosMessage.HsToObserver);
                    }
                    PulseSignalLocked();
                    break;

                case StocMessage.DuelStart:
                    if (_state == AgentSessionState.Lobby)
                        _state = AgentSessionState.InDuel;
                    _lastError = null;
                    PulseSignalLocked();
                    break;

                case StocMessage.TypeChange:
                    int tc = packet.ReadByte();
                    int pos = tc & 0xF;
                    _isHost = ((tc >> 4) & 0xF) != 0;
                    _isObserver = pos >= 8 || pos > 3;
                    _seatIndex = _isObserver ? -1 : pos;
                    PulseSignalLocked();
                    break;

                case StocMessage.HsPlayerEnter:
                    string enterName = packet.ReadUnicode(20);
                    int enterPos = packet.ReadByte();
                    if (enterPos >= 0 && enterPos < 4)
                        _slotNames[enterPos] = enterName;
                    AddEventLocked($"玩家 {enterName} 进入席位 #{enterPos}。");
                    PulseSignalLocked();
                    break;

                case StocMessage.HsPlayerChange:
                    int change = packet.ReadByte();
                    int cPos = (change >> 4) & 0xF;
                    int cState = change & 0xF;
                    if (cPos < 4)
                    {
                        if (cState < 4)
                        {
                            string? moved = _slotNames[cPos];
                            _slotNames[cPos] = null;
                            _slotNames[cState] = moved;
                            _slotReady[cPos] = false;
                            _slotReady[cState] = false;
                        }
                        else if (cState == (int)PlayerChange.Ready)
                        {
                            _slotReady[cPos] = true;
                            if (cPos == _seatIndex)
                                _lastError = null;
                        }
                        else if (cState == (int)PlayerChange.NotReady)
                            _slotReady[cPos] = false;
                        else if (cState is (int)PlayerChange.Leave or (int)PlayerChange.Observe)
                        {
                            _slotReady[cPos] = false;
                            _slotNames[cPos] = null;
                        }

                        bool allReady = _slotReady[0] && _slotReady[1] && (!_isTag || (_slotReady[2] && _slotReady[3]));
                        if (_isHost && _autoStartWhenAllReady && allReady)
                            _client.Send(CtosMessage.HsStart);
                    }
                    PulseSignalLocked();
                    break;

                case StocMessage.SelectHand:
                    _state = AgentSessionState.InDuel;
                    SetPendingPromptLocked(
                        "rock_paper_scissors",
                        "猜拳决定先后攻选择权",
                        canCancel: false,
                        minSelect: 1,
                        maxSelect: 1,
                        supportsMulti: false,
                        [
                            new AgentLegalActionChoice(0, "rps", "出剪刀 (Scissors)"),
                            new AgentLegalActionChoice(1, "rps", "出石头 (Rock)"),
                            new AgentLegalActionChoice(2, "rps", "出布 (Paper)")
                        ],
                        (idx, _) =>
                        {
                            if (idx is null or < 0 or > 2)
                                return (false, "猜拳请选择 actionIndex 0（剪刀）、1（石头）或 2（布）。");
                            _client.Send(CtosMessage.HandResult, (byte)(idx.Value + 1));
                            return (true, idx.Value switch { 0 => "猜拳：剪刀", 1 => "猜拳：石头", _ => "猜拳：布" });
                        });
                    break;

                case StocMessage.SelectTp:
                    _state = AgentSessionState.InDuel;
                    SetPendingPromptLocked(
                        "select_turn_order",
                        "猜拳获胜，请选择先攻或后攻",
                        canCancel: false,
                        minSelect: 1,
                        maxSelect: 1,
                        supportsMulti: false,
                        [
                            new AgentLegalActionChoice(0, "turn_order", "选择先攻 (Go First)"),
                            new AgentLegalActionChoice(1, "turn_order", "选择后攻 (Go Second)")
                        ],
                        (idx, _) =>
                        {
                            if (idx is null or < 0 or > 1)
                                return (false, "请选择 actionIndex 0（先攻）或 1（后攻）。");
                            _client.Send(CtosMessage.TpResult, (byte)(idx.Value == 0 ? 1 : 0));
                            return (true, idx.Value == 0 ? "选择先攻" : "选择后攻");
                        });
                    break;

                case StocMessage.TimeLimit:
                {
                    int tlPlayer = GetLocalPlayer(packet.ReadByte());
                    if (packet.BaseStream.Length - packet.BaseStream.Position >= 3)
                    {
                        packet.ReadByte(); // C++ padding
                        short leftSec = packet.ReadInt16();
                        if (tlPlayer == 0)
                            _remainingTimeSeconds = Math.Max(0, (int)leftSec);
                    }
                    if (tlPlayer == 0 && !_isObserver)
                        _client.Send(CtosMessage.TimeConfirm);
                    break;
                }

                case StocMessage.ChangeSide:
                    _state = AgentSessionState.SideDecking;
                    SetPendingPromptLocked(
                        "change_side",
                        "Match 副卡组更换阶段：可直接确认沿用当前卡组，或先调用 duel_set_deck 再确认",
                        canCancel: false,
                        minSelect: 1,
                        maxSelect: 1,
                        supportsMulti: false,
                        [new AgentLegalActionChoice(0, "confirm_side", "确认使用当前卡组进入下一局")],
                        (idx, _) =>
                        {
                            if (idx is not (null or 0))
                                return (false, "请提交 actionIndex = 0 确认副卡组。");
                            if (_lastSubmittedDeck is not null)
                                SendDeckPacketLocked(_lastSubmittedDeck);
                            return (true, "已确认当前副卡组");
                        },
                        allowAutoForced: false);
                    break;

                case StocMessage.TeammateSurrender:
                    _teammateSurrenderPending = true;
                    AddEventLocked("【2v2 队友协同】你的搭档发起了投降征询！若同意认输，请在本回合调用 duel_surrender。");
                    PulseSignalLocked();
                    break;

                case StocMessage.DuelEnd:
                    _state = AgentSessionState.DuelEnded;
                    _pendingPrompt = null;
                    AddEventLocked("整场对局已结束（STOC_DUEL_END）。");
                    PulseSignalLocked();
                    break;

                case StocMessage.ErrorMsg:
                {
                    int errType = packet.ReadByte();
                    int errCode = 0;
                    long rem = packet.BaseStream.Length - packet.BaseStream.Position;
                    if (rem >= 7)
                    {
                        packet.ReadBytes(3);
                        errCode = packet.ReadInt32();
                    }
                    else if (rem >= 4)
                    {
                        errCode = packet.ReadInt32();
                    }

                    _lastError = DecodeServerErrorMessage(errType, errCode, _cardRepoProvider());
                    AddEventLocked($"[错误提示] {_lastError}");
                    PulseSignalLocked();
                    break;
                }

                case StocMessage.Chat:
                {
                    int senderCode = packet.ReadUInt16();
                    string chatText = packet.ReadUnicode(256);
                    HandleInboundChatLocked(senderCode, chatText);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Packet parse warning in AgentDuelSession {SessionId}.", SessionId);
        }
    }

    private void HandleInboundChatLocked(int senderCode, string chatText)
    {
        if (string.IsNullOrWhiteSpace(chatText))
            return;

        string trimmed = chatText.Trim();

        // 解析同队私密《卡片力量》战术指令包：[PartnerTactic|stance|senderName] ...
        if (trimmed.StartsWith("[PartnerTactic|", StringComparison.Ordinal))
        {
            int closeBracket = trimmed.IndexOf(']');
            if (closeBracket > 15)
            {
                string header = trimmed[1..closeBracket];
                string[] parts = header.Split('|', 3);
                if (parts.Length == 3)
                {
                    string stance = parts[1];
                    string sender = parts[2];
                    string body = trimmed[(closeBracket + 1)..].Trim();
                    _partnerTactic = new AgentPartnerTactic(stance, body, sender, DateTime.UtcNow);
                    AddEventLocked($"[2v2 搭档战术] {body}");
                    return;
                }
            }
        }

        (string speakerName, string relation, int? seat) = ResolveChatSpeakerInfo(senderCode);
        long cSeq = ++_chatSeqCounter;
        if (_chatHistory.Count >= ChatHistoryCap)
            _chatHistory.RemoveAt(0);
        _chatHistory.Add(new AgentChatEntry(cSeq, speakerName, relation, seat, trimmed));

        string relLabel = relation switch
        {
            "self" => "我方",
            "teammate" => "队友",
            "enemy" => "敌方",
            "system" => "系统",
            _ => "观众"
        };
        AddEventLocked($"[聊天·{relLabel}:{speakerName}] {trimmed}");
    }

    private void HandleGameMessageLocked(GameMessage gm, BinaryReader packet)
    {
        INamedCardRepository cardRepo = _cardRepoProvider();

        switch (gm)
        {
            case GameMessage.Start:
            {
                int type = packet.ReadByte();
                _isFirst = (type & 0xF) == 0;
                _isObserver = (type & 0xF0) != 0 || _isObserver;
                int roomTeam = _isTag ? Math.Max(0, _seatIndex) / 2 : Math.Max(0, _seatIndex);
                _chatOrderSwapped = roomTeam != (_isFirst ? 0 : 1);
                _masterRule = packet.ReadByte();
                int lp0 = packet.ReadInt32();
                int lp1 = packet.ReadInt32();
                int d0 = packet.ReadInt16();
                int e0 = packet.ReadInt16();
                int d1 = packet.ReadInt16();
                int e1 = packet.ReadInt16();

                SideTracker side0 = GetSide(GetLocalPlayer(0));
                SideTracker side1 = GetSide(GetLocalPlayer(1));
                side0.Reset(lp0, d0, e0);
                side1.Reset(lp1, d1, e1);

                _turnCount = 0;
                _phase = DuelPhase.Draw;
                _duelResult = null;
                _lastError = null;
                _teammateSurrenderPending = false;
                _echoedChainSignature = null;
                _chain.Clear();
                _state = AgentSessionState.InDuel;
                _activeTagDeckIsMine = !_isTag || (_seatIndex >= 0 && (_isFirst ? _seatIndex % 2 == 0 : _seatIndex % 2 == 1));
                AddEventLocked($"对局正式开始！我方={(_isFirst ? "先攻" : "后攻")}，初始 LP={_self.LifePoints}:{_oppo.LifePoints}。");
                PulseSignalLocked();
                break;
            }

            case GameMessage.Win:
            {
                int winnerRaw = packet.ReadByte();
                int reason = packet.ReadByte();
                int localWinner = winnerRaw == 2 ? 2 : GetLocalPlayer(winnerRaw);
                _duelResult = localWinner switch
                {
                    0 => "Victory",
                    1 => "Defeat",
                    _ => "Draw"
                };
                _pendingPrompt = null;
                AddEventLocked($"本局胜负已分：{_duelResult}（reason=0x{reason:X}）。");
                PulseSignalLocked();
                break;
            }

            case GameMessage.Retry:
            {
                _lastError = "服务端返回 MSG_RETRY（上次动作响应参数不被引擎接受，请重新查看当前待响应提示）。";
                AddEventLocked(_lastError);
                PulseSignalLocked();
                break;
            }

            case GameMessage.Hint:
            {
                int hType = packet.ReadByte();
                packet.ReadByte(); // player
                int hData = packet.ReadInt32();
                if (hType == 3)
                    _selectHint = hData;
                break;
            }

            case GameMessage.NewTurn:
            {
                _turnCount++;
                _currentTurnPlayer = GetLocalPlayer(packet.ReadByte());
                _teammateSurrenderPending = false;
                _echoedChainSignature = null;
                _lastError = null;
                AddEventLocked($"=== 第 {_turnCount} 回合（{(_currentTurnPlayer == 0 ? "我方回合" : "对方回合")}）===");
                break;
            }

            case GameMessage.NewPhase:
            {
                _phase = (DuelPhase)packet.ReadInt16();
                _selectHint = 0;
                _echoedChainSignature = null;
                _lastError = null;
                AddEventLocked($"进入阶段：{_phase}");
                break;
            }

            case GameMessage.Draw:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int count = packet.ReadByte();
                SideTracker side = GetSide(p);
                List<string> drawnNames = new();
                for (int i = 0; i < count; i++)
                {
                    uint raw = packet.ReadUInt32();
                    int code = (int)(raw & 0x7fffffff);
                    if (side.Deck.Count > 0)
                        side.Deck.RemoveAt(side.Deck.Count - 1);
                    side.Hand.Add(new TrackedCard
                    {
                        Code = code,
                        Location = CardLocation.Hand,
                        Sequence = side.Hand.Count,
                        Position = (raw & 0x80000000) != 0 ? (int)CardPosition.FaceUp : (int)CardPosition.FaceDown
                    });
                    if (code > 0)
                        drawnNames.Add(LookupCardName(cardRepo, code));
                }

                AddEventLocked(drawnNames.Count > 0
                    ? $"{(p == 0 ? "我方" : "对方")}抽了 {count} 张卡：{string.Join("、", drawnNames)}。"
                    : $"{(p == 0 ? "我方" : "对方")}抽了 {count} 张卡。");
                break;
            }

            case GameMessage.TagSwap:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int mcount = packet.ReadByte();
                int ecount = packet.ReadByte();
                int pcount = packet.ReadByte();
                int hcount = packet.ReadByte();
                packet.ReadInt32(); // topcode (ignored to preserve strict fog-of-war)

                SideTracker side = GetSide(p);
                if (p == 0)
                    _activeTagDeckIsMine = !_activeTagDeckIsMine;

                side.Deck.Clear();
                for (int i = 0; i < mcount; i++)
                    side.Deck.Add(new TrackedCard { Code = 0, Location = CardLocation.Deck, Sequence = i, Position = (int)CardPosition.FaceDownDefence });

                side.Hand.Clear();
                for (int i = 0; i < hcount; i++)
                {
                    uint raw = packet.ReadUInt32();
                    int code = (int)(raw & 0x7fffffff);
                    side.Hand.Add(new TrackedCard
                    {
                        Code = code,
                        Location = CardLocation.Hand,
                        Sequence = i,
                        Position = (raw & 0x80000000) != 0 ? (int)CardPosition.FaceUp : (int)CardPosition.FaceDown
                    });
                }

                side.Extra.Clear();
                int faceupStart = Math.Max(0, ecount - pcount);
                for (int i = 0; i < ecount; i++)
                {
                    uint raw = packet.ReadUInt32();
                    int code = (int)(raw & 0x7fffffff);
                    bool faceup = i >= faceupStart || (raw & 0x80000000) != 0;
                    side.Extra.Add(new TrackedCard
                    {
                        Code = code,
                        Location = CardLocation.Extra,
                        Sequence = i,
                        Position = faceup ? (int)CardPosition.FaceUpDefence : (int)CardPosition.FaceDownDefence
                    });
                }
                AddEventLocked($"{(p == 0 ? "我方" : "对方")}进行 2v2 双打换人交接（当前我方执子主控={_activeTagDeckIsMine}）。");
                break;
            }

            case GameMessage.Damage:
            case GameMessage.PayLpCost:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int amt = packet.ReadInt32();
                SideTracker side = GetSide(p);
                side.LifePoints = Math.Max(0, side.LifePoints - amt);
                AddEventLocked($"{(p == 0 ? "我方" : "对方")} LP -{amt}（剩余 {side.LifePoints}）。");
                break;
            }

            case GameMessage.Recover:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int amt = packet.ReadInt32();
                SideTracker side = GetSide(p);
                side.LifePoints += amt;
                AddEventLocked($"{(p == 0 ? "我方" : "对方")} LP +{amt}（当前 {side.LifePoints}）。");
                break;
            }

            case GameMessage.LpUpdate:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int val = packet.ReadInt32();
                GetSide(p).LifePoints = val;
                break;
            }

            case GameMessage.Move:
            {
                int code = packet.ReadInt32();
                int pc = GetLocalPlayer(packet.ReadByte());
                int pl = packet.ReadByte();
                int ps = packet.ReadSByte();
                int pp = packet.ReadByte();
                int cc = GetLocalPlayer(packet.ReadByte());
                int cl = packet.ReadByte();
                int cs = packet.ReadSByte();
                int cp = packet.ReadByte();
                packet.ReadInt32(); // reason

                _echoedChainSignature = null;
                TrackedCard? moving = null;
                if ((pl & (int)CardLocation.Overlay) != 0)
                {
                    TrackedCard? host = GetSide(pc).GetCard((CardLocation)(pl & 0x7f), ps);
                    if (host is not null && pp >= 0 && pp < host.Overlays.Count)
                        host.Overlays.RemoveAt(pp);
                    moving = new TrackedCard { Code = code };
                }
                else if (pl != 0)
                {
                    moving = GetSide(pc).RemoveCard((CardLocation)pl, ps);
                }

                moving ??= new TrackedCard();
                if (code != 0 || cl == (int)CardLocation.Deck || (cp & (int)CardPosition.FaceDown) != 0 && cc == 1)
                    moving.Code = code;

                if ((cl & (int)CardLocation.Overlay) != 0)
                {
                    TrackedCard? targetHost = GetSide(cc).GetCard((CardLocation)(cl & 0x7f), cs);
                    targetHost?.Overlays.Add(code);
                }
                else if (cl != 0)
                {
                    GetSide(cc).AddCard((CardLocation)cl, cs, cp, moving);
                }

                if (cl != (int)CardLocation.Deck)
                {
                    string cName = code > 0 ? LookupCardName(cardRepo, code) : "盖卡";
                    AddEventLocked($"{(cc == 0 ? "我方" : "对方")} {cName} 移动到 {(CardLocation)(cl & 0x7f)}[{cs}]。");
                }
                break;
            }

            case GameMessage.PosChange:
            {
                int code = packet.ReadInt32();
                int pc = GetLocalPlayer(packet.ReadByte());
                int pl = packet.ReadByte();
                int ps = packet.ReadSByte();
                packet.ReadSByte(); // prev pos
                int cp = packet.ReadSByte();
                TrackedCard? c = GetSide(pc).GetCard((CardLocation)pl, ps);
                if (c is not null)
                {
                    if (code != 0)
                        c.Code = code;
                    c.Position = cp;
                }
                break;
            }

            case GameMessage.ShuffleHand:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                packet.ReadByte();
                SideTracker side = GetSide(p);
                foreach (TrackedCard c in side.Hand)
                    c.Code = packet.ReadInt32();
                break;
            }

            case GameMessage.ShuffleDeck:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                foreach (TrackedCard c in GetSide(p).Deck)
                    c.Code = 0;
                break;
            }

            case GameMessage.Chaining:
            {
                int code = packet.ReadInt32();
                int pcc = GetLocalPlayer(packet.ReadByte());
                int pcl = packet.ReadByte();
                int pcs = packet.ReadSByte();
                packet.ReadSByte(); // subs
                int cc = GetLocalPlayer(packet.ReadByte());
                packet.ReadInt16(); // trigger
                int desc = packet.ReadInt32();

                _echoedChainSignature = null;
                TrackedCard? c = GetSide(pcc).GetCard((CardLocation)(pcl & 0x7f), pcs);
                if (c is not null && code != 0)
                    c.Code = code;
                int effectiveCode = code != 0 ? code : (c?.Code ?? 0);
                string name = LookupCardName(cardRepo, effectiveCode);
                string? effText = ResolveEffectDescription(cardRepo, desc, effectiveCode);
                _chain.Add(new AgentChainLinkView(_chain.Count + 1, cc == 0 ? "self" : "opponent", effectiveCode, name, ((CardLocation)(pcl & 0x7f)).ToString(), desc, effText));
                AddEventLocked($"连锁 {_chain.Count}：{(cc == 0 ? "我方" : "对方")} 发动「{name}」({(CardLocation)(pcl & 0x7f)}){(string.IsNullOrEmpty(effText) ? "" : $" [{effText}]")}。");
                break;
            }

            case GameMessage.ChainEnd:
                _chain.Clear();
                break;

            case GameMessage.Attack:
            {
                int ca = GetLocalPlayer(packet.ReadByte());
                int la = packet.ReadByte();
                int sa = packet.ReadByte();
                packet.ReadByte();
                int cd = GetLocalPlayer(packet.ReadByte());
                int ld = packet.ReadByte();
                int sd = packet.ReadByte();
                packet.ReadByte();
                TrackedCard? atk = GetSide(ca).GetCard((CardLocation)la, sa);
                TrackedCard? def = ld == 0 ? null : GetSide(cd).GetCard((CardLocation)ld, sd);
                string atkName = atk is { Code: > 0 } ? LookupCardName(cardRepo, atk.Code) : "怪兽";
                string defName = def is null ? "直接攻击" : (def.Code > 0 ? LookupCardName(cardRepo, def.Code) : "里侧守备怪兽");
                AddEventLocked($"{(ca == 0 ? "我方" : "对方")}「{atkName}」攻击 -> {defName}。");
                break;
            }

            case GameMessage.UpdateData:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                CardLocation loc = (CardLocation)packet.ReadByte();
                IList<TrackedCard>? list = GetSide(p).GetZoneCollection(loc);
                if (list is not null)
                {
                    foreach (TrackedCard card in list)
                    {
                        if (packet.BaseStream.Position + 4 > packet.BaseStream.Length)
                            break;
                        int len = packet.ReadInt32();
                        long pos = packet.BaseStream.Position;
                        if (len > 8)
                            card.ReadQueryBlock(packet, GetLocalPlayer);
                        long next = pos + len - 4;
                        if (next >= 0 && next <= packet.BaseStream.Length)
                            packet.BaseStream.Position = next;
                        else
                            break;
                    }
                }
                break;
            }

            case GameMessage.UpdateCard:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                CardLocation loc = (CardLocation)packet.ReadByte();
                int seq = packet.ReadByte();
                int len = packet.ReadInt32();
                if (len > 8)
                {
                    TrackedCard? card = GetSide(p).GetCard(loc, seq);
                    card?.ReadQueryBlock(packet, GetLocalPlayer);
                }
                break;
            }

            case GameMessage.ConfirmCards:
            {
                packet.ReadByte(); // player
                packet.ReadByte(); // skip_panel
                int count = packet.ReadByte();
                List<string> revealed = new(count);
                for (int i = 0; i < count; i++)
                {
                    int code = packet.ReadInt32();
                    int p = GetLocalPlayer(packet.ReadByte());
                    CardLocation loc = (CardLocation)packet.ReadByte();
                    int seq = packet.ReadByte();
                    TrackedCard? card = GetSide(p).GetCard(loc, seq);
                    if (card is not null && code > 0)
                        card.Code = code;
                    revealed.Add(LookupCardName(cardRepo, code));
                }
                if (revealed.Count > 0)
                    AddEventLocked($"公开确认卡片：{string.Join("、", revealed)}。");
                break;
            }

            // ==================== 交互决策询问 ====================
            case GameMessage.SelectIdleCmd:
                ParseSelectIdleCmdLocked(packet, cardRepo);
                break;

            case GameMessage.SelectBattleCmd:
                ParseSelectBattleCmdLocked(packet, cardRepo);
                break;

            case GameMessage.SelectChain:
                ParseSelectChainLocked(packet, cardRepo);
                break;

            case GameMessage.SelectEffectYn:
                ParseSelectEffectYnLocked(packet, cardRepo);
                break;

            case GameMessage.SelectYesNo:
                ParseSelectYesNoLocked(packet, cardRepo);
                break;

            case GameMessage.SelectPosition:
                ParseSelectPositionLocked(packet, cardRepo);
                break;

            case GameMessage.SelectOption:
                ParseSelectOptionLocked(packet, cardRepo);
                break;

            case GameMessage.SelectCard:
                ParseSelectCardLocked(packet, cardRepo, isTribute: false);
                break;

            case GameMessage.SelectTribute:
                ParseSelectCardLocked(packet, cardRepo, isTribute: true);
                break;

            case GameMessage.SelectUnselect:
                ParseSelectUnselectLocked(packet, cardRepo);
                break;

            case GameMessage.SelectSum:
                ParseSelectSumLocked(packet, cardRepo);
                break;

            case GameMessage.SelectPlace:
                ParseSelectPlaceLocked(packet, "select_place");
                break;

            case GameMessage.SelectDisfield:
                ParseSelectPlaceLocked(packet, "select_disfield");
                break;

            case GameMessage.SelectCounter:
                ParseSelectCounterLocked(packet, cardRepo);
                break;

            case GameMessage.SortCard:
                ParseSortCardLocked(packet, cardRepo);
                break;

            case GameMessage.SortChain:
                SendResponseInt32Locked(-1);
                break;

            case GameMessage.AnnounceAttrib:
                ParseAnnounceAttribLocked(packet);
                break;

            case GameMessage.AnnounceRace:
                ParseAnnounceRaceLocked(packet);
                break;

            case GameMessage.AnnounceNumber:
                ParseAnnounceNumberLocked(packet);
                break;

            case GameMessage.AnnounceCard:
                ParseAnnounceCardLocked(packet, cardRepo);
                break;

            case GameMessage.RockPaperScissors:
                packet.ReadByte();
                SetPendingPromptLocked(
                    "duel_rps",
                    "卡片效果猜拳",
                    canCancel: false,
                    minSelect: 1,
                    maxSelect: 1,
                    supportsMulti: false,
                    [
                        new AgentLegalActionChoice(0, "rps", "剪刀"),
                        new AgentLegalActionChoice(1, "rps", "石头"),
                        new AgentLegalActionChoice(2, "rps", "布")
                    ],
                    (idx, _) =>
                    {
                        if (idx is null or < 0 or > 2)
                            return (false, "请选择 0、1 或 2。");
                        SendResponseInt32Locked(idx.Value + 1);
                        return (true, $"效果猜拳：{idx.Value + 1}");
                    });
                break;
        }
    }

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

            tagCtx = new AgentTagTeamContext(
                MySeatIndex: _seatIndex,
                MyTeamId: myTeam,
                PartnerSeatIndex: partnerSeat,
                PartnerName: teammate,
                EnemyNames: enemies,
                CurrentTurnRelation: turnRel,
                NextTurnHint: nextHint);
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

    private sealed class TrackedCard
    {
        public int Code { get; set; }
        public CardLocation Location { get; set; }
        public int Sequence { get; set; }
        public int Position { get; set; }
        public int? Attack { get; set; }
        public int? Defense { get; set; }
        public int? Level { get; set; }
        public List<int> Overlays { get; } = new();

        public void ReadQueryBlock(BinaryReader reader, Func<int, int> mapPlayer)
        {
            int flag = reader.ReadInt32();
            if ((flag & (int)Query.Code) != 0)
                Code = reader.ReadInt32();
            if ((flag & (int)Query.Position) != 0)
            {
                mapPlayer(reader.ReadByte());
                Location = (CardLocation)reader.ReadByte();
                Sequence = reader.ReadByte();
                Position = reader.ReadByte();
            }
            if ((flag & (int)Query.Alias) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Type) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Level) != 0) Level = reader.ReadInt32() & 0xff;
            if ((flag & (int)Query.Rank) != 0)
            {
                int r = reader.ReadInt32() & 0xff;
                if (r > 0) Level = r;
            }
            if ((flag & (int)Query.Attribute) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Race) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Attack) != 0) Attack = reader.ReadInt32();
            if ((flag & (int)Query.Defence) != 0) Defense = reader.ReadInt32();
            if ((flag & (int)Query.BaseAttack) != 0) reader.ReadInt32();
            if ((flag & (int)Query.BaseDefence) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Reason) != 0) reader.ReadInt32();
            if ((flag & (int)Query.ReasonCard) != 0) reader.ReadInt32();
            if ((flag & (int)Query.EquipCard) != 0) reader.ReadInt32();
            if ((flag & (int)Query.TargetCard) != 0)
            {
                int tc = reader.ReadInt32();
                for (int i = 0; i < tc; i++) reader.ReadInt32();
            }
            if ((flag & (int)Query.OverlayCard) != 0)
            {
                Overlays.Clear();
                int oc = reader.ReadInt32();
                for (int i = 0; i < oc; i++) Overlays.Add(reader.ReadInt32());
            }
            if ((flag & (int)Query.Counters) != 0)
            {
                int cc = reader.ReadInt32();
                for (int i = 0; i < cc; i++) reader.ReadInt32();
            }
            if ((flag & (int)Query.Owner) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Status) != 0) reader.ReadInt32();
            if ((flag & (int)Query.LScale) != 0) reader.ReadInt32();
            if ((flag & (int)Query.RScale) != 0) reader.ReadInt32();
            if ((flag & (int)Query.Link) != 0)
            {
                int lc = reader.ReadInt32();
                reader.ReadInt32();
                if (lc > 0) Level = lc;
            }
        }
    }

    private sealed class SideTracker
    {
        public int LifePoints { get; set; } = 8000;
        public List<TrackedCard> Deck { get; } = new();
        public List<TrackedCard> Hand { get; } = new();
        public List<TrackedCard> Grave { get; } = new();
        public List<TrackedCard> Banished { get; } = new();
        public List<TrackedCard> Extra { get; } = new();
        public TrackedCard?[] MonsterZone { get; } = new TrackedCard?[7];
        public TrackedCard?[] SpellZone { get; } = new TrackedCard?[8];

        public void Reset(int lp, int deckCount, int extraCount)
        {
            LifePoints = lp;
            Deck.Clear();
            Hand.Clear();
            Grave.Clear();
            Banished.Clear();
            Extra.Clear();
            Array.Clear(MonsterZone);
            Array.Clear(SpellZone);

            for (int i = 0; i < deckCount; i++)
                Deck.Add(new TrackedCard { Code = 0, Location = CardLocation.Deck, Sequence = i, Position = (int)CardPosition.FaceDownDefence });
            for (int i = 0; i < extraCount; i++)
                Extra.Add(new TrackedCard { Code = 0, Location = CardLocation.Extra, Sequence = i, Position = (int)CardPosition.FaceDownDefence });
        }

        public TrackedCard? GetCard(CardLocation loc, int seq)
        {
            return loc switch
            {
                CardLocation.MonsterZone => seq >= 0 && seq < MonsterZone.Length ? MonsterZone[seq] : null,
                CardLocation.SpellZone => seq >= 0 && seq < SpellZone.Length ? SpellZone[seq] : null,
                CardLocation.Hand => seq >= 0 && seq < Hand.Count ? Hand[seq] : null,
                CardLocation.Grave => seq >= 0 && seq < Grave.Count ? Grave[seq] : null,
                CardLocation.Removed => seq >= 0 && seq < Banished.Count ? Banished[seq] : null,
                CardLocation.Extra => seq >= 0 && seq < Extra.Count ? Extra[seq] : null,
                CardLocation.Deck => seq >= 0 && seq < Deck.Count ? Deck[seq] : null,
                _ => null
            };
        }

        public TrackedCard? RemoveCard(CardLocation loc, int seq)
        {
            switch (loc)
            {
                case CardLocation.MonsterZone when seq >= 0 && seq < MonsterZone.Length:
                {
                    TrackedCard? c = MonsterZone[seq];
                    MonsterZone[seq] = null;
                    return c;
                }
                case CardLocation.SpellZone when seq >= 0 && seq < SpellZone.Length:
                {
                    TrackedCard? c = SpellZone[seq];
                    SpellZone[seq] = null;
                    return c;
                }
                case CardLocation.Hand when seq >= 0 && seq < Hand.Count:
                {
                    TrackedCard c = Hand[seq];
                    Hand.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Grave when seq >= 0 && seq < Grave.Count:
                {
                    TrackedCard c = Grave[seq];
                    Grave.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Removed when seq >= 0 && seq < Banished.Count:
                {
                    TrackedCard c = Banished[seq];
                    Banished.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Extra when seq >= 0 && seq < Extra.Count:
                {
                    TrackedCard c = Extra[seq];
                    Extra.RemoveAt(seq);
                    return c;
                }
                case CardLocation.Deck when Deck.Count > 0:
                {
                    int idx = seq >= 0 && seq < Deck.Count ? seq : Deck.Count - 1;
                    TrackedCard c = Deck[idx];
                    Deck.RemoveAt(idx);
                    return c;
                }
                default:
                    return null;
            }
        }

        public void AddCard(CardLocation loc, int seq, int pos, TrackedCard card)
        {
            card.Location = loc;
            card.Sequence = seq;
            card.Position = pos;
            switch (loc)
            {
                case CardLocation.MonsterZone when seq >= 0 && seq < MonsterZone.Length:
                    MonsterZone[seq] = card;
                    break;
                case CardLocation.SpellZone when seq >= 0 && seq < SpellZone.Length:
                    SpellZone[seq] = card;
                    break;
                case CardLocation.Hand:
                    Hand.Add(card);
                    break;
                case CardLocation.Grave:
                    Grave.Add(card);
                    break;
                case CardLocation.Removed:
                    Banished.Add(card);
                    break;
                case CardLocation.Extra:
                    Extra.Add(card);
                    break;
                case CardLocation.Deck:
                    Deck.Add(card);
                    break;
            }
        }

        public IList<TrackedCard>? GetZoneCollection(CardLocation loc) => loc switch
        {
            CardLocation.Hand => Hand,
            CardLocation.Grave => Grave,
            CardLocation.Removed => Banished,
            CardLocation.Extra => Extra,
            CardLocation.Deck => Deck,
            CardLocation.MonsterZone => MonsterZone.Where(static c => c is not null).Cast<TrackedCard>().ToList(),
            CardLocation.SpellZone => SpellZone.Where(static c => c is not null).Cast<TrackedCard>().ToList(),
            _ => null
        };

        public AgentFieldSideView BuildView(INamedCardRepository repo, AgentObservationDetail detail)
        {
            bool includeEffects = detail == AgentObservationDetail.Full;
            bool includeGraveList = detail != AgentObservationDetail.Minimal;
            int faceUpExtra = Extra.Count(static c => (c.Position & (int)CardPosition.FaceUp) != 0);

            IReadOnlyList<AgentCardView>? knownExtra = null;
            if (detail == AgentObservationDetail.Full)
            {
                var allKnown = Extra
                    .Select((c, idx) => (Card: c, Idx: idx))
                    .Where(static x => x.Card.Code > 0)
                    .Select(x => ToCardView(x.Card, x.Idx, "Extra", repo, includeEffects: true))
                    .ToArray();
                if (allKnown.Length > 0)
                    knownExtra = allKnown;
            }
            else if (faceUpExtra > 0)
            {
                knownExtra = Extra
                    .Select((c, idx) => (Card: c, Idx: idx))
                    .Where(static x => x.Card.Code > 0 && (x.Card.Position & (int)CardPosition.FaceUp) != 0)
                    .Select(x => ToCardView(x.Card, x.Idx, "Extra", repo, includeEffects: false))
                    .ToArray();
            }

            return new AgentFieldSideView(
                LifePoints: LifePoints,
                DeckCount: Deck.Count,
                HandCount: Hand.Count,
                ExtraCount: Extra.Count,
                FaceUpExtraCount: faceUpExtra,
                GraveCount: Grave.Count,
                BanishedCount: Banished.Count,
                Hand: Hand.Select((c, idx) => ToCardView(c, idx, "Hand", repo, includeEffects)).ToArray(),
                MonsterZone: MonsterZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "MonsterZone", repo, includeEffects))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                SpellZone: SpellZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "SpellZone", repo, includeEffects))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                Graveyard: includeGraveList ? Grave.Select((c, idx) => ToCardView(c, idx, "Grave", repo, includeEffects)).ToArray() : null,
                Banished: includeGraveList ? Banished.Select((c, idx) => ToCardView(c, idx, "Removed", repo, includeEffects)).ToArray() : null,
                KnownExtraDeck: knownExtra);
        }

        private static AgentCardView ToCardView(TrackedCard c, int index, string loc, INamedCardRepository repo, bool includeEffects)
        {
            bool hidden = c.Code <= 0;
            NamedCard? nc = !hidden && repo.TryGetCard(c.Code, out NamedCard? found) ? found : null;
            string? shortDesc = null;
            if (includeEffects && nc is not null)
            {
                shortDesc = nc.Description;
                if (shortDesc is { Length: > 160 })
                    shortDesc = shortDesc[..160] + "...";
            }

            return new AgentCardView(
                Index: index,
                Code: hidden ? 0 : c.Code,
                Name: hidden ? null : (nc?.Name ?? $"#{c.Code}"),
                Location: loc,
                Sequence: c.Sequence,
                Position: ((CardPosition)c.Position).ToString(),
                IsHidden: hidden,
                Attack: hidden ? null : (c.Attack ?? ((nc is not null && (nc.Type & (int)CardType.Monster) != 0) ? nc.Attack : null)),
                Defense: hidden ? null : (c.Defense ?? ((nc is not null && (nc.Type & (int)CardType.Monster) != 0) ? nc.Defense : null)),
                Level: hidden ? null : (c.Level ?? (nc is not null && (nc.Type & (int)CardType.Monster) != 0 ? (int)(nc.Level & 0xff) : null)),
                OverlayCount: c.Overlays.Count,
                ShortEffect: shortDesc);
        }
    }
}
