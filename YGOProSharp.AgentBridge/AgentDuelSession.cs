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
    private readonly ILogger _logger = AppLog.CreateLogger<AgentDuelSession>();
    private readonly object _sync = new();
    private readonly YGOClient _client;
    private readonly Func<INamedCardRepository> _cardRepoProvider;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly Task _pumpTask;
    private readonly short _clientVersion;
    private readonly bool _joinAsObserver;

    private TaskCompletionSource<bool> _turnSignal = CreateSignal();
    private bool _disposed;
    private bool _sentJoinHandshake;
    private bool _requestedObserverOnJoin;

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
    private string? _duelResult;
    private string? _lastError;

    private readonly SideTracker _self = new();
    private readonly SideTracker _oppo = new();
    private readonly List<AgentChainLinkView> _chain = new();
    private readonly List<string> _events = new();
    private List<AgentDeckCountSummary> _ownSubmittedDeckSummary = new();
    private YdkDeckData? _lastSubmittedDeck;

    // 当前待响应动作
    private long _nextPromptId;
    private PendingPromptInternal? _pendingPrompt;
    private DateTime _lastChatSentUtc = DateTime.MinValue;

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

    public AgentObservation GetObservation()
    {
        lock (_sync)
            return BuildObservationLocked();
    }

    public AgentPendingPrompt? GetPendingPrompt()
    {
        lock (_sync)
            return _pendingPrompt?.PublicPrompt;
    }

    public async Task<AgentObservation> WaitForTurnAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + (timeout <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(50) : timeout);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            Task waitTask;
            lock (_sync)
            {
                if (_pendingPrompt is not null ||
                    _teammateSurrenderPending ||
                    _state is AgentSessionState.DuelEnded or AgentSessionState.Disconnected)
                {
                    return BuildObservationLocked();
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
            return BuildObservationLocked();
    }

    public async Task<bool> WaitUntilJoinedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                if (_state is AgentSessionState.Lobby or AgentSessionState.InDuel)
                    return true;
                if (_state == AgentSessionState.Disconnected)
                    return false;
            }
            await Task.Delay(15, cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
            return _state is AgentSessionState.Lobby or AgentSessionState.InDuel;
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

            SendDeckPacketLocked(deckData);
            _lastSubmittedDeck = deckData;
            _ownSubmittedDeckSummary = BuildUnorderedDeckSummary(deckData, _cardRepoProvider());

            if (_state == AgentSessionState.SideDecking && _pendingPrompt?.PublicPrompt.PromptType == "change_side")
            {
                _pendingPrompt = null;
            }

            AddEventLocked($"已提交卡组（主卡组 {deckData.Main.Count} 张 / 额外 {deckData.Extra.Count} 张 / 副卡组 {deckData.Side.Count} 张）。");
            return (true, $"已提交卡组（主 {deckData.Main.Count} / 额外 {deckData.Extra.Count} / 副 {deckData.Side.Count}）。");
        }
    }

    public (bool Ok, string Message) SetReady(bool ready, bool startIfHost = false)
    {
        lock (_sync)
        {
            if (_isObserver)
                return (false, "观战席无法切换准备状态。");
            if (_state != AgentSessionState.Lobby)
                return (false, $"当前状态为 {_state}，仅可在大厅阶段准备或取消准备。");

            _autoStartWhenAllReady = ready && startIfHost;
            _client.Send(ready ? CtosMessage.HsReady : CtosMessage.HsNotReady);
            if (ready && startIfHost && _isHost && AreOtherSeatsReadyLocked())
                _client.Send(CtosMessage.HsStart);

            return (true, ready ? "已发送准备指令。" : "已取消准备。");
        }
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

    public (bool Ok, string Message) RequestStartDuel()
    {
        lock (_sync)
        {
            if (!_isHost)
                return (false, "当前会话不是房主，无法发起开局。");
            if (_state != AgentSessionState.Lobby)
                return (false, "当前不在大厅等待阶段。");

            _client.Send(CtosMessage.HsStart);
            return (true, "已作为房主发送开局请求（需全员已准备）。");
        }
    }

    public (bool Ok, string Message) SwapSeat(bool toObserver = false)
    {
        lock (_sync)
        {
            if (_state != AgentSessionState.Lobby)
                return (false, "仅可在大厅未开局阶段切换席位。");

            _client.Send(toObserver ? CtosMessage.HsToObserver : CtosMessage.HsToDuelist);
            return (true, toObserver ? "已请求切换到观战席。" : "已请求切换决斗席位/队伍。");
        }
    }

    public (bool Ok, string Message) SendChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return (false, "聊天内容不能为空。");

        string trimmed = message.Trim();
        if (trimmed.Length > 200)
            trimmed = trimmed[..200];

        lock (_sync)
        {
            if (!IsConnected)
                return (false, "会话已断开连接。");

            DateTime now = DateTime.UtcNow;
            if ((now - _lastChatSentUtc).TotalMilliseconds < 600)
                return (false, "发言过快，请稍后再发送聊天。");

            _lastChatSentUtc = now;
            SendChatLocked(trimmed);
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
            AddEventLocked(_isTag ? "已发送投降指令（2v2 模式下需队友同回合确认）。" : "已发送投降指令。");
            return (true, _isTag ? "已发送 2v2 投降请求（若队友已发起则立即生效，否则等待队友确认）。" : "已投降。");
        }
    }

    public AgentActionResult SubmitAction(
        int? actionIndex,
        IReadOnlyList<int>? cardIndices = null,
        long? expectedPromptId = null,
        string? thoughtSummary = null,
        string? chatMessage = null)
    {
        AgentThoughtNotification? thoughtToPublish = null;
        AgentActionResult result;

        lock (_sync)
        {
            if (_pendingPrompt is null)
            {
                return new AgentActionResult(
                    false,
                    "当前没有等待您响应的动作询问，请先调用 duel_wait_for_turn。",
                    null,
                    BuildObservationLocked());
            }

            PendingPromptInternal current = _pendingPrompt;
            if (expectedPromptId.HasValue && expectedPromptId.Value != current.PublicPrompt.PromptId)
            {
                return new AgentActionResult(
                    false,
                    $"动作询问编号已过期（期望 {expectedPromptId.Value}，当前最新为 {current.PublicPrompt.PromptId}），请根据最新 observation 重新选择。",
                    null,
                    BuildObservationLocked());
            }

            (bool ok, string errOrDesc) = current.TryExecute(actionIndex, cardIndices);
            if (!ok)
            {
                return new AgentActionResult(
                    false,
                    errOrDesc,
                    null,
                    BuildObservationLocked());
            }

            long handledId = current.PublicPrompt.PromptId;
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
                DateTime now = DateTime.UtcNow;
                if ((now - _lastChatSentUtc).TotalMilliseconds >= 500)
                {
                    _lastChatSentUtc = now;
                    string cleanChat = chatMessage.Trim();
                    if (cleanChat.Length > 200)
                        cleanChat = cleanChat[..200];
                    SendChatLocked(cleanChat);
                }
            }

            result = new AgentActionResult(
                true,
                $"已提交动作：{errOrDesc}",
                handledId,
                BuildObservationLocked());
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

        return result;
    }

    private async Task RunPumpLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                lock (_sync)
                {
                    if (!_sentJoinHandshake && _client.IsConnected)
                        SendInitialHandshakeLocked();

                    _client.Update();
                }

                await Task.Delay(10, ct).ConfigureAwait(false);
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
                    AddEventLocked($"成功加入房间 {RoomName}（Mode={_roomMode}, MR{_masterRule}）。");
                    if (_joinAsObserver && !_requestedObserverOnJoin)
                    {
                        _requestedObserverOnJoin = true;
                        _client.Send(CtosMessage.HsToObserver);
                    }
                    PulseSignalLocked();
                    break;

                case StocMessage.TypeChange:
                    int tc = packet.ReadByte();
                    int pos = tc & 0xF;
                    _isHost = ((tc >> 4) & 0xF) != 0;
                    _isObserver = pos >= 8 || pos > 3;
                    _seatIndex = _isObserver ? -1 : pos;
                    break;

                case StocMessage.HsPlayerEnter:
                    string enterName = packet.ReadUnicode(20);
                    int enterPos = packet.ReadByte();
                    if (enterPos >= 0 && enterPos < 4)
                        _slotNames[enterPos] = enterName;
                    AddEventLocked($"玩家 {enterName} 进入席位 #{enterPos}。");
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
                            _slotReady[cPos] = true;
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
                    break;

                case StocMessage.SelectHand:
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
                    int tlPlayer = GetLocalPlayer(packet.ReadByte());
                    if (tlPlayer == 0 && !_isObserver)
                        _client.Send(CtosMessage.TimeConfirm);
                    break;

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
                        });
                    break;

                case StocMessage.TeammateSurrender:
                    _teammateSurrenderPending = true;
                    AddEventLocked("【2v2 队友协同】你的队友发起了投降征询！若同意认输，请在本回合调用 duel_surrender。");
                    PulseSignalLocked();
                    break;

                case StocMessage.DuelEnd:
                    _state = AgentSessionState.DuelEnded;
                    _pendingPrompt = null;
                    AddEventLocked("整场对局已结束（STOC_DUEL_END）。");
                    PulseSignalLocked();
                    break;

                case StocMessage.ErrorMsg:
                    int errType = packet.ReadByte();
                    packet.ReadBytes(3);
                    int errCode = packet.ReadInt32();
                    _lastError = $"Server ErrorMsg type={errType}, code=0x{errCode:X}";
                    AddEventLocked($"收到服务端错误提示：{_lastError}");
                    PulseSignalLocked();
                    break;

                case StocMessage.Chat:
                    int senderCode = packet.ReadUInt16();
                    string chatText = packet.ReadUnicode(256);
                    string speaker = ResolveChatSpeakerName(senderCode);
                    AddEventLocked($"[聊天] {speaker}: {chatText}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Packet parse warning in AgentDuelSession {SessionId}.", SessionId);
        }
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
                _teammateSurrenderPending = false;
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
                _lastError = "服务端返回 MSG_RETRY（上次动作响应不被引擎接受）。";
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
                AddEventLocked($"=== 第 {_turnCount} 回合（{(_currentTurnPlayer == 0 ? "我方回合" : "对方回合")}）===");
                break;
            }

            case GameMessage.NewPhase:
            {
                _phase = (DuelPhase)packet.ReadInt16();
                _selectHint = 0;
                AddEventLocked($"进入阶段：{_phase}");
                break;
            }

            case GameMessage.Draw:
            {
                int p = GetLocalPlayer(packet.ReadByte());
                int count = packet.ReadByte();
                SideTracker side = GetSide(p);
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
                }
                AddEventLocked($"{(p == 0 ? "我方" : "对方")}抽了 {count} 张卡。");
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
                AddEventLocked($"{(p == 0 ? "我方" : "对方")}进行 2v2 双打换人交接（当前我方主控={_activeTagDeckIsMine}）。");
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

                if (code > 0 && cl != (int)CardLocation.Deck)
                {
                    string cName = LookupCardName(cardRepo, code);
                    AddEventLocked($"{(cc == 0 ? "我方" : "对方")} {cName} 移动到 {(CardLocation)(cl & 0x7f)}。");
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
                TrackedCard? c = GetSide(pcc).GetCard((CardLocation)(pcl & 0x7f), pcs);
                if (c is not null && code != 0)
                    c.Code = code;
                string name = LookupCardName(cardRepo, code != 0 ? code : (c?.Code ?? 0));
                _chain.Add(new AgentChainLinkView(_chain.Count + 1, cc == 0 ? "self" : "opponent", code, name, ((CardLocation)(pcl & 0x7f)).ToString(), desc));
                AddEventLocked($"连锁 {_chain.Count}：{(cc == 0 ? "我方" : "对方")} 发动 {name}。");
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
                AddEventLocked($"{(ca == 0 ? "我方" : "对方")} {atkName} 攻击 -> {defName}。");
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
                    AddEventLocked($"公开确认卡片：{string.Join(", ", revealed)}。");
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
                ParseSelectYesNoLocked(packet);
                break;

            case GameMessage.SelectPosition:
                ParseSelectPositionLocked(packet, cardRepo);
                break;

            case GameMessage.SelectOption:
                ParseSelectOptionLocked(packet);
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
                string name = LookupCardName(repo, realCode);
                int rawVal = (i << 16) + k;
                string label = $"{categoryVerb[k]}：{name} ({loc}[{seq}])";
                choices.Add(new AgentLegalActionChoice(choices.Count, categoryKind[k], label, realCode, name, $"{loc}[{seq}]"));
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
            string name = LookupCardName(repo, realCode);
            int rawVal = (i << 16) + 5;
            string label = $"发动效果：{name} ({loc}[{seq}], desc={desc})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "activate", label, realCode, name, $"{loc}[{seq}]", desc));
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
            string name = LookupCardName(repo, realCode);
            string label = $"发动战斗阶段效果：{name} ({loc}[{seq}])";
            choices.Add(new AgentLegalActionChoice(choices.Count, "activate", label, realCode, name, $"{loc}[{seq}]", desc));
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
            string name = LookupCardName(repo, realCode);
            string atkInfo = tracked?.Attack is not null ? $" ATK={tracked.Attack}" : string.Empty;
            string label = $"宣言攻击：{name} ({loc}[{seq}]{atkInfo}{(dirAtt ? ", 可直接攻击" : "")})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "attack", label, realCode, name, $"{loc}[{seq}]"));
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
            "战斗阶段操作选择",
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

        List<(int ChainIdx, int Code, string Name, string Zone, int Desc, bool Forced)> items = new(count);
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
            items.Add((i, realCode, LookupCardName(repo, realCode), $"{(con == 0 ? "Self" : "Oppo")}.{loc}[{seq}]", desc, forced));
        }

        if (items.Count == 0)
        {
            SendResponseInt32Locked(-1);
            return;
        }

        if (items.Count == 1 && items[0].Forced)
        {
            SendResponseInt32Locked(0);
            return;
        }

        bool anyForced = items.Any(static x => x.Forced);
        List<AgentLegalActionChoice> choices = new();
        List<(int RawResponse, string Desc)> map = new();

        if (!anyForced)
        {
            choices.Add(new AgentLegalActionChoice(0, "pass_chain", "不连锁 / 放弃时点 (Pass)"));
            map.Add((-1, "不连锁 (Pass)"));
        }

        foreach (var item in items)
        {
            string label = $"连锁发动：{item.Name} ({item.Zone}, desc={item.Desc}{(item.Forced ? ", 必发" : "")})";
            choices.Add(new AgentLegalActionChoice(choices.Count, "chain_activate", label, item.Code, item.Name, item.Zone, item.Desc));
            map.Add((item.ChainIdx, label));
        }

        SetPendingPromptLocked(
            "select_chain",
            "连锁/自由时点响应询问",
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
            });
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
        string name = LookupCardName(repo, code);

        SetPendingPromptLocked(
            "select_effect_yn",
            $"是否发动 {name} ({loc}[{seq}]) 的效果？",
            canCancel: true,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            [
                new AgentLegalActionChoice(0, "yes", $"是：发动 {name} 的效果", code, name, $"{con}:{loc}[{seq}]", desc),
                new AgentLegalActionChoice(1, "no", $"否：不发动 {name} 的效果", code, name)
            ],
            (idx, _) =>
            {
                if (idx is null or < 0 or > 1)
                    return (false, "请选择 0（发动）或 1（不发动）。");
                SendResponseInt32Locked(idx.Value == 0 ? 1 : 0);
                return (true, idx.Value == 0 ? $"发动 {name}" : $"不发动 {name}");
            });
    }

    private void ParseSelectYesNoLocked(BinaryReader packet)
    {
        packet.ReadByte();
        int desc = packet.ReadInt32();
        string hint = desc switch
        {
            30 => "战斗卷回，是否继续攻击？",
            31 => "是否直接攻击对方玩家？",
            _ => $"确认是/否询问 (desc={desc})"
        };

        SetPendingPromptLocked(
            "select_yes_no",
            hint,
            canCancel: true,
            minSelect: 1,
            maxSelect: 1,
            supportsMulti: false,
            [
                new AgentLegalActionChoice(0, "yes", "是 (Yes)"),
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
            return;
        }

        SetPendingPromptLocked(
            "select_position",
            $"请选择 {name} 的表示形式",
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

    private void ParseSelectOptionLocked(BinaryReader packet)
    {
        packet.ReadByte();
        int count = packet.ReadByte();
        List<AgentLegalActionChoice> choices = new(count);
        for (int i = 0; i < count; i++)
        {
            int optCode = packet.ReadInt32();
            choices.Add(new AgentLegalActionChoice(i, "option", $"选项 #{i} (desc={optCode})", EffectDescription: optCode));
        }

        SetPendingPromptLocked(
            "select_option",
            "请选择一项效果分支选项",
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
                return (true, $"选择选项 #{idx.Value}");
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
            string name = realCode > 0 ? LookupCardName(repo, realCode) : "里侧未知卡";
            string zone = $"{(con == 0 ? "我方" : "对方")}.{loc}[{seq}]";
            candidates.Add((realCode, name, zone, Math.Max(1, param)));
            choices.Add(new AgentLegalActionChoice(i, isTribute ? "tribute_card" : "card", $"[{i}] {name} ({zone})", realCode > 0 ? realCode : null, name, zone));
        }

        int cancelActionIdx = -1;
        if (cancelable)
        {
            cancelActionIdx = choices.Count;
            choices.Add(new AgentLegalActionChoice(cancelActionIdx, "cancel", "取消 / 完成选择 (Cancel)"));
        }

        int hint = _selectHint;
        _selectHint = 0;

        SetPendingPromptLocked(
            isTribute ? "select_tribute" : "select_card",
            $"请选择 {min} ~ {max} 张卡片（hint={hint}；多张请通过 cardIndices 传下标数组，单张可直接传 actionIndex）",
            canCancel: cancelable,
            minSelect: min,
            maxSelect: max,
            supportsMulti: max > 1,
            choices,
            (idx, multi) =>
            {
                if (cancelable && idx == cancelActionIdx && (multi is null || multi.Count == 0))
                {
                    SendResponseInt32Locked(-1);
                    return (true, "取消选卡");
                }

                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count == 0 && cancelable)
                {
                    SendResponseInt32Locked(-1);
                    return (true, "取消选卡");
                }

                if (picked.Any(x => x < 0 || x >= candidates.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"选卡下标必须在 0 ~ {candidates.Count - 1} 之间且不重复。");

                if (!isTribute && (picked.Count < min || picked.Count > max))
                    return (false, $"本次必须选择 {min} ~ {max} 张卡（当前提交了 {picked.Count} 张；如需多选请传 cardIndices 数组）。");

                if (isTribute)
                {
                    int weightSum = picked.Sum(i => candidates[i].TributeWeight);
                    if (weightSum < min || picked.Count > max)
                        return (false, $"解放祭品数量不满足要求（需要至少 {min} 祭品、最多 {max} 张）。");
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
            "逐步选卡（每次选 1 张，满足条件后可选择 Finish 完成）",
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
            $"请选择卡片使等级/数值之和{(exact ? "等于" : "至少达到")} {targetSum - mustSum}（传 cardIndices 数组）",
            canCancel: false,
            minSelect: Math.Max(1, min),
            maxSelect: max,
            supportsMulti: true,
            choices,
            (idx, multi) =>
            {
                List<int> picked = ResolveSelectedIndices(idx, multi);
                if (picked.Count == 0 || picked.Any(x => x < 0 || x >= opts.Count) || picked.Distinct().Count() != picked.Count)
                    return (false, $"请通过 cardIndices 提供 0 ~ {opts.Count - 1} 的不重复下标。");

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

            int sideOffset = zone >> 4; // 0 = local self, 1 = local opponent
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
            "请宣言一张卡名（可从候选选择 actionIndex，或在 cardIndices[0] 直接传入合法卡密 ID）",
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
                if (idx is null || idx.Value < 0 || idx.Value >= choices.Count)
                    return (false, $"请选择 0 ~ {choices.Count - 1}，或在 cardIndices[0] 传入目标卡密。");
                int chosenCode = choices[idx.Value].CardCode ?? 89631139;
                SendResponseInt32Locked(chosenCode);
                return (true, $"宣言卡片：{choices[idx.Value].Label}");
            });
    }

    private AgentObservation BuildObservationLocked()
    {
        INamedCardRepository repo = _cardRepoProvider();
        List<AgentLobbySlotView> lobbySlots = new(_isTag ? 4 : 2);
        int maxSlots = _isTag ? 4 : 2;
        for (int i = 0; i < maxSlots; i++)
        {
            lobbySlots.Add(new AgentLobbySlotView(
                i,
                _isTag ? (i >= 2 ? 1 : 0) : i,
                _slotNames[i],
                _slotReady[i]));
        }

        string? teammate = null;
        if (_isTag && _seatIndex is >= 0 and < 4)
        {
            int partnerSeat = _seatIndex ^ 1;
            teammate = _slotNames[partnerSeat];
        }

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
            DuelResult: _duelResult,
            LastError: _lastError,
            LobbySlots: lobbySlots,
            SelfSide: _self.BuildView(repo),
            OpponentSide: _oppo.BuildView(repo),
            CurrentChain: _chain.ToArray(),
            OwnSubmittedDeckList: _ownSubmittedDeckSummary,
            RecentEvents: _events.ToArray(),
            HasPendingAction: _pendingPrompt is not null,
            PendingPrompt: _pendingPrompt?.PublicPrompt);
    }

    private void SetPendingPromptLocked(
        string promptType,
        string hintText,
        bool canCancel,
        int minSelect,
        int maxSelect,
        bool supportsMulti,
        IReadOnlyList<AgentLegalActionChoice> choices,
        Func<int?, IReadOnlyList<int>?, (bool Ok, string Message)> executor)
    {
        long id = ++_nextPromptId;
        AgentPendingPrompt pub = new(id, promptType, hintText, canCancel, minSelect, maxSelect, supportsMulti, choices);
        _pendingPrompt = new PendingPromptInternal(pub, executor);
        PulseSignalLocked();
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

    private string ResolveChatSpeakerName(int senderCode)
    {
        if (senderCode < 4)
        {
            int pos = senderCode;
            if (_chatOrderSwapped)
                pos = _isTag ? senderCode ^ 2 : 1 - senderCode;
            return _slotNames[pos] ?? $"Player#{pos}";
        }
        return senderCode == 8 || senderCode is >= 11 and <= 19 ? "System" : "Observer";
    }

    private void AddEventLocked(string text)
    {
        if (_events.Count >= 40)
            _events.RemoveAt(0);
        _events.Add(text);
    }

    private void PulseSignalLocked()
    {
        TaskCompletionSource<bool> prev = _turnSignal;
        _turnSignal = CreateSignal();
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

    private static string LookupCardName(INamedCardRepository repo, int code)
    {
        if (code <= 0)
            return "未知暗牌";
        return repo.TryGetCard(code, out NamedCard? nc) && nc is not null && !string.IsNullOrWhiteSpace(nc.Name)
            ? nc.Name
            : $"#{code}";
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
                list.Add(new AgentDeckCountSummary(g.Key, name, g.Count(), section));
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

    private sealed class PendingPromptInternal(
        AgentPendingPrompt publicPrompt,
        Func<int?, IReadOnlyList<int>?, (bool Ok, string Message)> handler)
    {
        public AgentPendingPrompt PublicPrompt { get; } = publicPrompt;
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

        public AgentFieldSideView BuildView(INamedCardRepository repo)
        {
            int faceUpExtra = Extra.Count(static c => (c.Position & (int)CardPosition.FaceUp) != 0);
            return new AgentFieldSideView(
                LifePoints: LifePoints,
                DeckCount: Deck.Count,
                HandCount: Hand.Count,
                ExtraCount: Extra.Count,
                FaceUpExtraCount: faceUpExtra,
                Hand: Hand.Select((c, idx) => ToCardView(c, idx, "Hand", repo)).ToArray(),
                MonsterZone: MonsterZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "MonsterZone", repo))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                SpellZone: SpellZone
                    .Select((c, idx) => c is null ? null : ToCardView(c, idx, "SpellZone", repo))
                    .Where(static x => x is not null)
                    .Cast<AgentCardView>()
                    .ToArray(),
                Graveyard: Grave.Select((c, idx) => ToCardView(c, idx, "Grave", repo)).ToArray(),
                Banished: Banished.Select((c, idx) => ToCardView(c, idx, "Removed", repo)).ToArray(),
                KnownExtraDeck: Extra
                    .Select((c, idx) => (Card: c, Idx: idx))
                    .Where(static x => x.Card.Code > 0)
                    .Select(x => ToCardView(x.Card, x.Idx, "Extra", repo))
                    .ToArray());
        }

        private static AgentCardView ToCardView(TrackedCard c, int index, string loc, INamedCardRepository repo)
        {
            bool hidden = c.Code <= 0;
            NamedCard? nc = !hidden && repo.TryGetCard(c.Code, out NamedCard? found) ? found : null;
            string? shortDesc = nc?.Description;
            if (shortDesc is { Length: > 180 })
                shortDesc = shortDesc[..180] + "...";

            return new AgentCardView(
                Index: index,
                Code: hidden ? 0 : c.Code,
                Name: hidden ? null : (nc?.Name ?? $"#{c.Code}"),
                Location: loc,
                Sequence: c.Sequence,
                Position: ((CardPosition)c.Position).ToString(),
                IsHidden: hidden,
                Attack: hidden ? null : (c.Attack ?? nc?.Attack),
                Defense: hidden ? null : (c.Defense ?? nc?.Defense),
                Level: hidden ? null : (c.Level ?? (nc is not null ? (int)(nc.Level & 0xff) : null)),
                OverlayCount: c.Overlays.Count,
                ShortEffect: hidden ? null : shortDesc);
        }
    }
}
