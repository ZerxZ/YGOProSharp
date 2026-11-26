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
                    string? handoff = _partnerTactic?.SharedBoardHandoffNote;
                    int handoffMarker = body.IndexOf("[交接:", StringComparison.Ordinal);
                    if (handoffMarker >= 0)
                    {
                        int endBr = body.IndexOf(']', handoffMarker + 4);
                        if (endBr > handoffMarker + 4)
                            handoff = body[(handoffMarker + 4)..endBr].Trim();
                    }
                    _partnerTactic = new AgentPartnerTactic(stance, body, sender, DateTime.UtcNow, handoff);
                    AddEventLocked($"[2v2 搭档战术] {body}");
                    PulseSignalLocked();
                    return;
                }
            }
        }

        (string speakerName, string relation, int? seat) = ResolveChatSpeakerInfo(senderCode);
        string? tacticalHint = relation == "teammate" ? DetectNaturalLanguageTacticalHint(trimmed) : null;
        long cSeq = ++_chatSeqCounter;
        if (_chatHistory.Count >= ChatHistoryCap)
            _chatHistory.RemoveAt(0);
        _chatHistory.Add(new AgentChatEntry(cSeq, speakerName, relation, seat, trimmed, tacticalHint));

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

    private static string? DetectNaturalLanguageTacticalHint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string lower = text.ToLowerInvariant();
        if (lower.Contains("otk", StringComparison.Ordinal) || text.Contains("斩杀", StringComparison.Ordinal) || text.Contains("总攻", StringComparison.Ordinal) || text.Contains("带走", StringComparison.Ordinal))
            return "all_in_otk";
        if (text.Contains("激进", StringComparison.Ordinal) || text.Contains("强攻", StringComparison.Ordinal) || text.Contains("抢血", StringComparison.Ordinal) || text.Contains("铺场", StringComparison.Ordinal))
            return "aggressive";
        if (text.Contains("留格", StringComparison.Ordinal) || text.Contains("留场", StringComparison.Ordinal) || text.Contains("铺垫", StringComparison.Ordinal) || text.Contains("留素材", StringComparison.Ordinal))
            return "setup_partner";
        if (text.Contains("稳住", StringComparison.Ordinal) || text.Contains("稳健", StringComparison.Ordinal) || text.Contains("留康", StringComparison.Ordinal) || text.Contains("护航", StringComparison.Ordinal))
            return "control";
        if (text.Contains("防守", StringComparison.Ordinal) || text.Contains("保命", StringComparison.Ordinal) || text.Contains("挡刀", StringComparison.Ordinal))
            return "defensive";
        return null;
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
}
