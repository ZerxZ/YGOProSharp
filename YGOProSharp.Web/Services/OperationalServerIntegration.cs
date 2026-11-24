using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;
using YGOProSharp.Server;

namespace YGOProSharp.Web.Services;

public sealed record RoomChatRecord(
    DateTime TimestampUtc,
    string SenderName,
    int SenderType,
    string Message);

public sealed record RoomPublicEventRecord(
    DateTime TimestampUtc,
    string EventType,
    string Summary);

/// <summary>
/// 单个房间的公开运行轨迹缓存（严格遵守战争迷雾：仅缓存公开聊天与公开阶段变更，绝不采集任何玩家暗手牌或洗牌牌序）。
/// </summary>
public sealed class RoomActivityState
{
    private readonly object _gate = new();
    private readonly Queue<RoomChatRecord> _chats = new();
    private readonly Queue<RoomPublicEventRecord> _events = new();
    private const int MaxEntries = 80;

    public string RoomName { get; }
    public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
    public DateTime? DuelStartedAtUtc { get; set; }
    public string? RawJoinPassword { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? OwnerUsername { get; set; }

    public RoomActivityState(string roomName)
    {
        RoomName = roomName;
    }

    public void AddChat(string senderName, int senderType, string message)
    {
        lock (_gate)
        {
            while (_chats.Count >= MaxEntries)
                _chats.Dequeue();
            _chats.Enqueue(new RoomChatRecord(DateTime.UtcNow, senderName, senderType, message));
        }
    }

    public void AddPublicEvent(string eventType, string summary)
    {
        lock (_gate)
        {
            while (_events.Count >= MaxEntries)
                _events.Dequeue();
            _events.Enqueue(new RoomPublicEventRecord(DateTime.UtcNow, eventType, summary));
        }
    }

    public IReadOnlyList<RoomChatRecord> GetChats()
    {
        lock (_gate)
            return _chats.ToArray();
    }

    public IReadOnlyList<RoomPublicEventRecord> GetPublicEvents()
    {
        lock (_gate)
            return _events.ToArray();
    }
}

/// <summary>
/// 解耦连接 <see cref="CoreServer"/> 与 <see cref="OperationalStore"/> 的协调服务：
/// 负责房间事件订阅、战报与云录像 <c>R#&lt;id&gt;</c> 落库、IP/昵称封禁拦截与 <c>vpass</c> 身份核验。
/// </summary>
public sealed class OperationalServerCoordinator : IDisposable
{
    private readonly ILogger<OperationalServerCoordinator> _logger = AppLog.CreateLogger<OperationalServerCoordinator>();
    private readonly OperationalStore _store;
    private readonly ConcurrentDictionary<string, RoomActivityState> _roomStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _agentGate = new();
    private AgentSessionManager? _agentSessionManager;

    public event Action<string, object>? OnPublicEventPublished;

    public OperationalServerCoordinator(OperationalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public AgentSessionManager GetOrCreateAgentSessionManager(
        YGOProSharpServerHost serverHost,
        Func<INamedCardRepository>? namedCardRepositoryProvider = null)
    {
        lock (_agentGate)
        {
            if (_agentSessionManager is not null)
                return _agentSessionManager;

            _agentSessionManager = new AgentSessionManager(
                () => serverHost.CreateLocalTransport(),
                () => namedCardRepositoryProvider?.Invoke() ?? EmptyNamedCardRepository.Instance,
                serverHost.Server.Options.ClientVersion);

            _agentSessionManager.OnThoughtPublished += thought =>
            {
                RoomActivityState state = GetOrCreateRoomState(thought.RoomName);
                state.AddPublicEvent("ai.thought", $"[AI 思考 · {thought.PlayerName}] {thought.Summary}");
                PublishEvent("ai.thought", new
                {
                    roomName = thought.RoomName,
                    playerName = thought.PlayerName,
                    turnCount = thought.TurnCount,
                    phase = thought.Phase,
                    summary = thought.Summary,
                    timestampUtc = thought.TimestampUtc
                });
            };

            return _agentSessionManager;
        }
    }

    public int RevokeMcpTokenSessions(Guid tokenId)
    {
        lock (_agentGate)
        {
            return _agentSessionManager?.RevokeSessionsByToken(tokenId) ?? 0;
        }
    }

    public void Dispose()
    {
        lock (_agentGate)
        {
            _agentSessionManager?.Dispose();
            _agentSessionManager = null;
        }
    }

    public RoomActivityState GetOrCreateRoomState(string roomName)
    {
        string key = string.IsNullOrWhiteSpace(roomName) ? "default" : roomName.Trim();
        return _roomStates.GetOrAdd(key, static name => new RoomActivityState(name));
    }

    public RoomActivityState? TryGetRoomState(string roomName)
    {
        return _roomStates.TryGetValue(roomName, out RoomActivityState? state) ? state : null;
    }

    public void RemoveRoomState(string roomName)
    {
        _roomStates.TryRemove(roomName, out _);
    }

    public int CountOwnedActiveRooms(Guid userId, CoreServer server)
    {
        int count = 0;
        foreach (RoomSnapshot snap in server.GetRooms())
        {
            if (_roomStates.TryGetValue(snap.RoomName, out RoomActivityState? state) && state.OwnerUserId == userId)
                count++;
        }
        return count;
    }

    /// <summary>
    /// 生成可注入到 <see cref="ServerOptions.AddonFactories"/> 的房间监控与战报录制插件工厂。
    /// </summary>
    public Func<Game, AddonBase> CreateRoomAddonFactory()
    {
        return game => new OperationalRoomAddon(game, this, _store, _logger);
    }

    /// <summary>
    /// 生成可注入到 <see cref="ServerOptions.JoinAccessValidator"/> 的准入拦截器（封禁表 + vpass + 云录像 <c>R#&lt;id&gt;</c>）。
    /// </summary>
    public Func<PlayerJoinAccessContext, PlayerJoinAccessDecision> CreateJoinAccessValidator()
    {
        return context =>
        {
            Player player = context.Player;
            string? clientIp = player.RemoteAddress;

            // 1. 检查 IP / CIDR / 昵称封禁表（命中 L1/Redis 缓存）
            ActiveBanRule? matchedBan = _store.CheckBanMatchAsync(clientIp, player.Name, null)
                .GetAwaiter()
                .GetResult();
            if (matchedBan is not null)
            {
                _logger.LogWarning(
                    "Blocked banned player '{PlayerName}' ({ClientIp}) matching rule {BanType}:{BanValue}.",
                    player.Name,
                    clientIp,
                    matchedBan.TargetType,
                    matchedBan.TargetValue);
                return new PlayerJoinAccessDecision(false, $"您已被禁止进入服务器：{matchedBan.Reason}");
            }

            // 2. 核验客户端 昵称$密码 (vpass) 身份保护
            (bool vpassOk, string? vpassReason) = _store.VerifyPlayerVpassAsync(player.Name ?? string.Empty, player.Vpass)
                .GetAwaiter()
                .GetResult();
            if (!vpassOk)
            {
                return new PlayerJoinAccessDecision(false, vpassReason ?? "昵称密码校验失败。");
            }

            // 3. 支持客户端输入 R#<id> 口令直接拉取云录像 (.yrp)
            string trimmedPw = context.RoomPassword?.Trim() ?? string.Empty;
            if (!context.IsCreateGame &&
                trimmedPw.StartsWith("R#", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(trimmedPw.AsSpan(2), out long replayId) &&
                replayId > 0)
            {
                byte[]? yrpBytes = _store.GetReplayYrpBytesAsync(replayId).GetAwaiter().GetResult();
                if (yrpBytes is null || yrpBytes.Length == 0)
                {
                    return new PlayerJoinAccessDecision(false, $"未找到编号为 R#{replayId} 的云录像。");
                }

                SendCloudReplayPacket(player, replayId, yrpBytes);
                return new PlayerJoinAccessDecision(false, null, HandledCustomSession: true);
            }

            return new PlayerJoinAccessDecision(true);
        };
    }

    internal void PublishEvent(string eventType, object payload)
    {
        OnPublicEventPublished?.Invoke(eventType, payload);
    }

    private static void SendCloudReplayPacket(Player player, long replayId, byte[] yrpBytes)
    {
        string notice = $"[CloudReplay] 正在下发云录像 R#{replayId}（可在录像列表保存回放）。";
        using (BinaryWriter chat = GamePacketFactory.Create(StocMessage.Chat))
        {
            chat.Write((short)PlayerType.Yellow);
            chat.WriteUnicode(notice, notice.Length + 1);
            player.Send(chat);
        }

        using (BinaryWriter replayPkt = GamePacketFactory.Create(StocMessage.Replay))
        {
            replayPkt.Write(yrpBytes);
            player.Send(replayPkt);
        }

        using (BinaryWriter endPkt = GamePacketFactory.Create(StocMessage.DuelEnd))
        {
            player.Send(endPkt);
        }
    }

    private sealed class OperationalRoomAddon : AddonBase
    {
        private readonly OperationalServerCoordinator _coordinator;
        private readonly OperationalStore _store;
        private readonly ILogger _logger;
        private RoomActivityState? _state;
        private Deck?[] _capturedStartDecks = [];
        private bool _disposed;

        public OperationalRoomAddon(
            Game game,
            OperationalServerCoordinator coordinator,
            OperationalStore store,
            ILogger logger)
            : base(game)
        {
            _coordinator = coordinator;
            _store = store;
            _logger = logger;

            Game.OnNetworkReady += HandleNetworkReady;
            Game.OnPlayerJoin += HandlePlayerJoin;
            Game.OnPlayerLeave += HandlePlayerLeave;
            Game.OnPlayerMove += HandlePlayerMove;
            Game.OnPlayerReady += HandlePlayerReady;
            Game.OnPlayerChat += HandlePlayerChat;
            Game.OnGameStart += HandleGameStart;
            Game.OnBeforeDuelStart += HandleBeforeDuelStart;
            Game.OnDuelEnd += HandleDuelEnd;
            Game.OnGameEnd += HandleGameEnd;
        }

        private RoomActivityState EnsureState()
        {
            return _state ??= _coordinator.GetOrCreateRoomState(Game.RoomName);
        }

        private void HandleNetworkReady(object sender, EventArgs e)
        {
            EnsureState().AddPublicEvent("room.created", $"房间 {Game.RoomName} 已就绪。");
            _coordinator.PublishEvent("room.created", new { roomName = Game.RoomName });
        }

        private void HandlePlayerJoin(object sender, PlayerEventArgs e)
        {
            if (e.Player is not Player p)
                return;

            RoomActivityState state = EnsureState();
            if (string.IsNullOrEmpty(state.RawJoinPassword) && !string.IsNullOrEmpty(p.JoinPassword))
                state.RawJoinPassword = p.JoinPassword;

            state.AddPublicEvent("player.join", $"{p.Name} 加入了房间。");
            _coordinator.PublishEvent("room.updated", new { roomName = Game.RoomName, reason = "player.join", player = p.Name });
        }

        private void HandlePlayerLeave(object sender, PlayerEventArgs e)
        {
            if (e.Player is not Player p)
                return;

            EnsureState().AddPublicEvent("player.leave", $"{p.Name} 离开了房间。");
            _coordinator.PublishEvent("room.updated", new { roomName = Game.RoomName, reason = "player.leave", player = p.Name });
        }

        private void HandlePlayerMove(object sender, PlayerMoveEventArgs e)
        {
            if (e.Player is not Player p)
                return;

            string moveDesc = p.Type == (int)PlayerType.Observer
                ? $"{p.Name} 切换到了观战席。"
                : $"{p.Name} 坐上了决斗席 #{p.Type}。";
            EnsureState().AddPublicEvent("player.move", moveDesc);
            _coordinator.PublishEvent("room.updated", new { roomName = Game.RoomName, reason = "player.move", player = p.Name });
        }

        private void HandlePlayerReady(object sender, PlayerEventArgs e)
        {
            if (e.Player is not Player p)
                return;

            _coordinator.PublishEvent("room.updated", new { roomName = Game.RoomName, reason = "player.ready", player = p.Name });
        }

        private void HandlePlayerChat(object sender, PlayerChatEventArgs e)
        {
            if (e.Player is not Player p)
                return;

            EnsureState().AddChat(p.Name ?? "Player", p.Type, e.Message);
            _coordinator.PublishEvent("room.chat", new
            {
                roomName = Game.RoomName,
                sender = p.Name ?? "Player",
                senderType = p.Type,
                message = e.Message,
                timestampUtc = DateTime.UtcNow
            });
        }

        private void HandleGameStart(object sender, EventArgs e)
        {
            RoomActivityState state = EnsureState();
            state.DuelStartedAtUtc = DateTime.UtcNow;
            _capturedStartDecks = new Deck?[Game.Players.Length];
            for (int i = 0; i < Game.Players.Length; i++)
                _capturedStartDecks[i] = Game.Players[i]?.Deck;

            state.AddPublicEvent("duel.starting", "对局进入猜拳与先后攻阶段。");
            _coordinator.PublishEvent("duel.public_event", new { roomName = Game.RoomName, eventType = "duel.starting" });
        }

        private void HandleBeforeDuelStart(object sender, DuelStartContext e)
        {
            EnsureState().AddPublicEvent("duel.started", $"第 {Game.DuelCount + 1} 局正式开始（Seed={e.Seed}）。");
            _coordinator.PublishEvent("duel.public_event", new
            {
                roomName = Game.RoomName,
                eventType = "duel.started",
                duelIndex = Game.DuelCount + 1,
                mode = Game.Mode,
                randomDeckMode = Game.RandomDeckMode.ToString()
            });
        }

        private void HandleDuelEnd(object sender, EventArgs e)
        {
            int duelIdx = Math.Max(0, Game.DuelCount - 1);
            int duelWinner = duelIdx < Game.MatchResults.Length ? Game.MatchResults[duelIdx] : Game.Winner;
            EnsureState().AddPublicEvent("duel.ended", $"第 {Game.DuelCount} 局结束，胜方阵营={duelWinner}，回合数={Game.TurnCount}。");
            _coordinator.PublishEvent("duel.public_event", new
            {
                roomName = Game.RoomName,
                eventType = "duel.ended",
                duelCount = Game.DuelCount,
                turnCount = Game.TurnCount,
                winnerTeam = duelWinner
            });
        }

        private void HandleGameEnd(object sender, EventArgs e)
        {
            _ = PersistCompletedDuelRecordAsync();
        }

        private async Task PersistCompletedDuelRecordAsync()
        {
            try
            {
                if (Game.DuelCount <= 0 && Game.TurnCount <= 0 && Game.Replay is null)
                    return;

                RoomActivityState state = EnsureState();
                byte[]? yrpBytes = null;
                uint seed = 0;
                if (Game.Replay is { Disabled: false })
                {
                    seed = Game.Replay.Header.Seed;
                    try
                    {
                        yrpBytes = Game.Replay.GetContent();
                    }
                    catch
                    {
                        // Replay 尚未 End 时忽略字节导出错误
                    }
                }

                List<SaveDuelPlayerSlot> slots = new(Game.Players.Length);
                for (int i = 0; i < Game.Players.Length; i++)
                {
                    Player? p = Game.Players[i];
                    if (p is null)
                        continue;

                    int teamId = Game.IsTag ? (i >= 2 ? 1 : 0) : i;
                    Deck? startDeck = i < _capturedStartDecks.Length ? _capturedStartDecks[i] : p.Deck;
                    string? startYdk = startDeck is not null ? YdkSerializer.Serialize(startDeck) : null;
                    string? ingameYdk = p.Deck is not null ? YdkSerializer.Serialize(p.Deck) : null;

                    slots.Add(new SaveDuelPlayerSlot(
                        SeatIndex: i,
                        TeamId: teamId,
                        PlayerName: p.Name ?? $"Player{i}",
                        RealName: p.RawPlayerInfoName,
                        SeatType: p.SeatOrigin,
                        ClientIp: p.RemoteAddress,
                        StartDeckYdk: startYdk,
                        IngameDeckYdk: ingameYdk,
                        IsWinner: Game.Winner == teamId));
                }

                int lastDuelIdx = Math.Clamp(Game.DuelCount - 1, 0, Game.MatchReasons.Length - 1);
                int winReason = Game.DuelCount > 0 ? Game.MatchReasons[lastDuelIdx] : 0;

                string hostInfoJson = JsonSerializer.Serialize(new
                {
                    mode = Game.Mode,
                    region = Game.Region,
                    masterRule = Game.MasterRule,
                    startLp = Game.StartLp,
                    startHand = Game.StartHand,
                    drawCount = Game.DrawCount,
                    timer = Game.Timer,
                    banlist = Game.Banlist?.Name,
                    noCheckDeck = Game.NoCheckDeck,
                    noShuffleDeck = Game.NoShuffleDeck
                });

                DuelRecordEntity saved = await _store.SaveDuelRecordAsync(new SaveDuelRecordRequest(
                    RoomName: Game.RoomName,
                    RoomIdentifier: $"{Game.RoomName}_{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Mode: Game.Mode,
                    Region: Game.Region,
                    MasterRule: Game.MasterRule,
                    StartLp: Game.StartLp,
                    HostInfoJson: hostInfoJson,
                    RandomDeckMode: Game.RandomDeckMode.ToString(),
                    IsSuddenDeath: Game.IsSuddenDeath,
                    DuelCount: Math.Max(1, Game.DuelCount),
                    TurnCount: Game.TurnCount,
                    WinnerTeam: Game.Winner,
                    WinReason: winReason,
                    Seed: seed,
                    StartedAt: state.DuelStartedAtUtc ?? state.CreatedAtUtc,
                    EndedAt: DateTime.UtcNow,
                    YrpBytes: yrpBytes,
                    ObserverPacketFrames: null,
                    Players: slots)).ConfigureAwait(false);

                _coordinator.PublishEvent("replay.saved", new
                {
                    replayId = saved.Id,
                    replayCode = $"R#{saved.Id}",
                    roomName = saved.RoomName,
                    winnerTeam = saved.WinnerTeam
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist duel record for room '{RoomName}'.", Game.RoomName);
            }
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Game.OnNetworkReady -= HandleNetworkReady;
            Game.OnPlayerJoin -= HandlePlayerJoin;
            Game.OnPlayerLeave -= HandlePlayerLeave;
            Game.OnPlayerMove -= HandlePlayerMove;
            Game.OnPlayerReady -= HandlePlayerReady;
            Game.OnPlayerChat -= HandlePlayerChat;
            Game.OnGameStart -= HandleGameStart;
            Game.OnBeforeDuelStart -= HandleBeforeDuelStart;
            Game.OnDuelEnd -= HandleDuelEnd;
            Game.OnGameEnd -= HandleGameEnd;
            _coordinator.RemoveRoomState(Game.RoomName);
        }
    }
}

/// <summary>
/// 建房配置解析、标准主机密码拼接与安全观众快照辅助类（供管理后台与玩家面板共用）。
/// </summary>
public static class RoomConfigHelper
{
    public static string BuildTemplateRulePrefix(string? template, string? customRulePrefix)
    {
        string basePrefix = (template?.Trim().ToLowerInvariant()) switch
        {
            "match" => "M,MR5",
            "tag" or "2v2" => "2V2,MR5",
            "blind_box" => "RD,MR5,NC",
            "mirror_box" => "RDM,MR5,NC",
            "chaos" => "CHAOS,MR5,NC",
            "chaos60" => "CHAOS60,MR5,NC",
            "cricket_1v1" => "MR5,TM300,NC",
            "cricket_1v1_mirror" => "RDM,MR5,TM300,NC",
            "cricket_2v2" => "2V2,RDM,MR5,TM300,NC",
            "cricket_2v2_chaos" => "2V2,CHAOS,MR5,TM300,NC",
            "diy_test" => "DIY,MR5,NC,NF",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(customRulePrefix))
            return basePrefix;
        if (string.IsNullOrWhiteSpace(basePrefix))
            return customRulePrefix.Trim();

        return $"{basePrefix},{customRulePrefix.Trim()}";
    }

    public static GameOptions ResolveCreateRoomOptions(
        GameOptions baseGameOptions,
        RoomPasswordParser parser,
        string roomName,
        CreateRoomRequestDto body,
        out string canonicalFullHostPassword)
    {
        string rulePrefix = BuildTemplateRulePrefix(body.Template, body.RulePrefix);
        GameOptions opts = baseGameOptions;

        if (!string.IsNullOrWhiteSpace(rulePrefix))
        {
            string fullSpec = rulePrefix.Contains('#') ? rulePrefix : $"{rulePrefix}#{roomName}";
            ParsedRoomPassword parsed = parser.Parse(fullSpec);
            opts = parsed.ApplyTo(opts);
        }

        RoomRuleOverrides explicitOverrides = new();
        if (body.Mode.HasValue)
            explicitOverrides.Mode = Math.Clamp(body.Mode.Value, 0, 2);
        if (body.Region.HasValue)
            explicitOverrides.Region = Math.Clamp(body.Region.Value, 0, 5);
        if (body.MasterRule.HasValue)
            explicitOverrides.MasterRule = Math.Clamp(body.MasterRule.Value, 1, 5);
        if (body.NoBanlist == true || body.BanlistIndex is < 0)
        {
            explicitOverrides.NoBanlist = true;
            explicitOverrides.BanlistIndex = -1;
        }
        else if (body.BanlistIndex.HasValue)
        {
            explicitOverrides.NoBanlist = false;
            explicitOverrides.BanlistIndex = body.BanlistIndex.Value;
        }

        if (body.StartLp.HasValue)
            explicitOverrides.StartLp = Math.Clamp(body.StartLp.Value, 1, 999999);
        if (body.StartHand.HasValue)
            explicitOverrides.StartHand = Math.Clamp(body.StartHand.Value, 0, 40);
        if (body.DrawCount.HasValue)
            explicitOverrides.DrawCount = Math.Clamp(body.DrawCount.Value, 0, 35);
        if (body.GameTimer.HasValue)
            explicitOverrides.GameTimer = Math.Clamp(body.GameTimer.Value, 0, 9999);
        if (body.NoCheckDeck.HasValue)
            explicitOverrides.NoCheckDeck = body.NoCheckDeck.Value;
        if (body.NoShuffleDeck.HasValue)
            explicitOverrides.NoShuffleDeck = body.NoShuffleDeck.Value;
        if (body.EnablePriority.HasValue)
            explicitOverrides.EnablePriority = body.EnablePriority.Value;
        if (body.SuddenDeath.HasValue)
            explicitOverrides.SuddenDeath = body.SuddenDeath.Value;
        if (body.ChaosMainDeckSize.HasValue)
            explicitOverrides.ChaosMainDeckSize = Math.Clamp(body.ChaosMainDeckSize.Value, 20, 60);

        if (!string.IsNullOrWhiteSpace(body.RandomDeckMode))
        {
            string rm = body.RandomDeckMode.Trim();
            if (string.Equals(rm, "Chaos60", StringComparison.OrdinalIgnoreCase))
            {
                explicitOverrides.RandomDeck = RandomDeckMode.ChaosDraft;
                explicitOverrides.ChaosMainDeckSize = 60;
            }
            else if (Enum.TryParse(rm, ignoreCase: true, out RandomDeckMode parsedMode))
            {
                explicitOverrides.RandomDeck = parsedMode;
                if (parsedMode is RandomDeckMode.DiyRandom or RandomDeckMode.DiyChaos && !explicitOverrides.Region.HasValue && opts.Region == 0)
                    explicitOverrides.Region = 3;
            }
        }

        if (explicitOverrides.HasAnyOverride)
        {
            opts = explicitOverrides.ApplyTo(opts);
            canonicalFullHostPassword = BuildCanonicalHostPassword(opts, roomName, baseGameOptions, explicitOverrides);
        }
        else
        {
            canonicalFullHostPassword = string.IsNullOrWhiteSpace(rulePrefix)
                ? roomName
                : (rulePrefix.Contains('#') ? rulePrefix : $"{rulePrefix}#{roomName}");
        }

        return opts;
    }

    public static string FormatPublicRuleHostPassword(Game g, GameOptions? baseOptions = null)
    {
        List<string> tokens = new();
        if (g.Mode == 1) tokens.Add("M");
        else if (g.Mode == 2) tokens.Add("2V2");

        if (g.Region == 1) tokens.Add("TCG");
        else if (g.Region == 2) tokens.Add("OT");
        else if (g.Region == 3) tokens.Add("DIY");

        if (g.MasterRule != 5)
            tokens.Add($"MR{g.MasterRule}");

        switch (g.RandomDeckMode)
        {
            case RandomDeckMode.PresetRandom: tokens.Add("RD"); break;
            case RandomDeckMode.MirrorRandom: tokens.Add("RDM"); break;
            case RandomDeckMode.ChaosDraft:
                tokens.Add(g.ChaosMainDeckSize == 60 ? "CHAOS60" : "CHAOS");
                break;
            case RandomDeckMode.PureChaos: tokens.Add("PURECHAOS"); break;
            case RandomDeckMode.DiyRandom: tokens.Add("DIYRD"); break;
            case RandomDeckMode.DiyChaos: tokens.Add("DIYCHAOS"); break;
        }

        int defaultLp = g.IsTag ? Game.DEFAULT_TAG_LIFEPOINTS : Game.DEFAULT_LIFEPOINTS;
        if (g.StartLp != defaultLp)
            tokens.Add($"LP{g.StartLp}");
        if (g.StartHand != Game.DEFAULT_START_HAND)
            tokens.Add($"ST{g.StartHand}");
        if (g.DrawCount != Game.DEFAULT_DRAW_COUNT)
            tokens.Add($"DR{g.DrawCount}");
        if (g.Timer != Game.DEFAULT_TIMER)
            tokens.Add($"TM{g.Timer}");
        if (g.NoCheckDeck && (baseOptions is null || !baseOptions.NoCheckDeck))
            tokens.Add("NC");
        if (g.NoShuffleDeck && (baseOptions is null || !baseOptions.NoShuffleDeck))
            tokens.Add("NS");
        if (g.Banlist is null && BanlistManager.Banlists.Count > 0 && (baseOptions is null || baseOptions.Banlist >= 0))
            tokens.Add("NF");
        if (g.IsSuddenDeath && (baseOptions is null || !baseOptions.SuddenDeath))
            tokens.Add("ED");

        return tokens.Count == 0 ? g.RoomName : $"{string.Join(',', tokens)}#{g.RoomName}";
    }

    public static string BuildCanonicalHostPassword(
        GameOptions opts,
        string roomName,
        GameOptions? baseOptions = null,
        RoomRuleOverrides? explicitOverrides = null)
    {
        List<string> tokens = new();
        if (opts.Mode == 1) tokens.Add("M");
        else if (opts.Mode == 2) tokens.Add("2V2");

        if (opts.Region == 1) tokens.Add("TCG");
        else if (opts.Region == 2) tokens.Add("OT");
        else if (opts.Region == 3) tokens.Add("DIY");

        if (opts.MasterRule != 5)
            tokens.Add($"MR{opts.MasterRule}");

        switch (opts.RandomDeck)
        {
            case RandomDeckMode.PresetRandom: tokens.Add("RD"); break;
            case RandomDeckMode.MirrorRandom: tokens.Add("RDM"); break;
            case RandomDeckMode.ChaosDraft:
                tokens.Add(opts.ChaosMainDeckSize == 60 ? "CHAOS60" : "CHAOS");
                break;
            case RandomDeckMode.PureChaos: tokens.Add("PURECHAOS"); break;
            case RandomDeckMode.DiyRandom: tokens.Add("DIYRD"); break;
            case RandomDeckMode.DiyChaos: tokens.Add("DIYCHAOS"); break;
        }

        int defaultLp = opts.Mode == 2 ? Game.DEFAULT_TAG_LIFEPOINTS : Game.DEFAULT_LIFEPOINTS;
        if (opts.StartLp != defaultLp)
            tokens.Add($"LP{opts.StartLp}");
        if (opts.StartHand != Game.DEFAULT_START_HAND)
            tokens.Add($"ST{opts.StartHand}");
        if (opts.DrawCount != Game.DEFAULT_DRAW_COUNT)
            tokens.Add($"DR{opts.DrawCount}");
        if (opts.GameTimer != Game.DEFAULT_TIMER)
            tokens.Add($"TM{opts.GameTimer}");
        if (explicitOverrides?.NoCheckDeck == true || (opts.NoCheckDeck && (baseOptions is null || !baseOptions.NoCheckDeck)))
            tokens.Add("NC");
        if (explicitOverrides?.NoShuffleDeck == true || (opts.NoShuffleDeck && (baseOptions is null || !baseOptions.NoShuffleDeck)))
            tokens.Add("NS");
        if (explicitOverrides?.NoBanlist == true || (opts.Banlist < 0 && BanlistManager.Banlists.Count > 0 && (baseOptions is null || baseOptions.Banlist >= 0)))
            tokens.Add("NF");
        else if (explicitOverrides?.BanlistIndex is >= 0 && opts.Banlist >= 0 && (baseOptions is null || opts.Banlist != baseOptions.Banlist))
            tokens.Add($"LF{opts.Banlist + 1}");
        if (explicitOverrides?.SuddenDeath == true || (opts.SuddenDeath && (baseOptions is null || !baseOptions.SuddenDeath)))
            tokens.Add("ED");

        return tokens.Count == 0 ? roomName : $"{string.Join(',', tokens)}#{roomName}";
    }

    public static Player[] SnapshotObservers(Game g)
    {
        try
        {
            return g.Observers.Where(static o => o is not null).ToArray();
        }
        catch
        {
            return [];
        }
    }
}
