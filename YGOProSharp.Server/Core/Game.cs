using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core.Cards;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 房间与 duel 状态机（state machine）。网络 client 通过 <see cref="Player"/> 进入，native duel 工作留在 <see cref="IDuelSession"/> 后面。
    /// </summary>
    public class Game : IGame
    {
        public const int DEFAULT_LIFEPOINTS = 8000;
        public const int DEFAULT_START_HAND = 5;
        public const int DEFAULT_DRAW_COUNT = 1;
        public const int DEFAULT_TIMER = 240;

        public Banlist Banlist { get; private set; } = null!;
        public int Mode { get; set; }
        public int Region { get; set; }
        public int MasterRule { get; set; }
        public int StartLp { get; set; }
        public int StartHand { get; set; }
        public int DrawCount { get; set; }
        public int Timer { get; set; }
        public bool EnablePriority { get; set; }
        public bool NoCheckDeck { get; set; }
        public bool NoShuffleDeck { get; set; }
        public bool IsMatch { get; private set; }
        public bool IsTag { get; private set; }
        public bool IsTpSelect { get; private set; }

        public GameState State { get; private set; }
        public DateTime SideTimer { get; private set; }
        public DateTime TpTimer { get; private set; }
        public DateTime RpsTimer { get; private set; }
        public int TurnCount { get; set; }
        public int CurrentPlayer { get; set; }
        public int[] LifePoints { get; set; }

        public Player[] Players { get; private set; }
        public Player[] CurPlayers { get; private set; }
        public bool[] IsReady { get; private set; }
        public List<Player> Observers { get; private set; }
        public Player HostPlayer { get; private set; } = null!;

        public Replay Replay { get; private set; } = null!;
        public int Winner { get; private set; }
        public int[] MatchResults { get; private set; }
        public int[] MatchReasons { get; private set; }
        public int DuelCount;
        public ICardRepository CardRepository { get; }

        private CoreServer _server;
        private readonly IDuelFactory? _duelFactory;
        private readonly ILogger<Game> _logger;
        private readonly GameOptions _options;
        // Native session 生命周期从 lobby 进入 duel 时开始，并在 EndDuel/End 结束。
        private IDuelSession _duel = null!;
        private GameAnalyser _analyser;
        private int[] _handResult;
        private int _startplayer;
        private int _lastresponse;

        private int[] _timelimit;
        private DateTime? _time;
        
        private bool _matchKill;

        public event Action<object, EventArgs>? OnNetworkReady;
        public event Action<object, EventArgs>? OnNetworkEnd;
        public event Action<object, EventArgs>? OnGameStart;
        public event Action<object, EventArgs>? OnGameEnd;
        public event Action<object, EventArgs>? OnDuelEnd;
        public event Action<object, PlayerEventArgs>? OnPlayerJoin;
        public event Action<object, PlayerEventArgs>? OnPlayerLeave;
        public event Action<object, PlayerMoveEventArgs>? OnPlayerMove;
        public event Action<object, PlayerEventArgs>? OnPlayerReady;
        public event Action<object, PlayerChatEventArgs>? OnPlayerChat;

        /// <summary>
        /// 使用领域依赖创建 game room；socket 和 native 实现细节留在本类之外。
        /// </summary>
        public Game(CoreServer server, IDuelFactory? duelFactory = null, ICardRepository? cardRepository = null, GameOptions? options = null)
        {
            _duelFactory = duelFactory;
            _logger = AppLog.CreateLogger<Game>();
            _options = options ?? new GameOptions();
            CardRepository = cardRepository ?? EmptyCardRepository.Instance;
            State = GameState.Lobby;
            Mode = _options.Mode;
            Region = _options.Region;
            MasterRule = _options.MasterRule;

            IsMatch = Mode == 1;
            IsTag = Mode == 2;
            CurrentPlayer = 0;
            LifePoints = new int[2];
            Players = new Player[IsTag ? 4 : 2];
            CurPlayers = new Player[2];
            IsReady = new bool[IsTag ? 4 : 2];
            _handResult = new int[2];
            _timelimit = new int[2];
            Winner = -1;
            MatchResults = new int[3];
            MatchReasons = new int[3];
            Observers = new List<Player>();

            int lfList = _options.Banlist;
            if (lfList >= 0 && lfList < BanlistManager.Banlists.Count)
                Banlist = BanlistManager.Banlists[lfList];

            StartLp = _options.StartLp;
            LifePoints[0] = StartLp;
            LifePoints[1] = StartLp;
            StartHand = _options.StartHand;
            DrawCount = _options.DrawCount;
            EnablePriority = _options.EnablePriority;
            NoCheckDeck = _options.NoCheckDeck;
            NoShuffleDeck = _options.NoShuffleDeck;
            Timer = _options.GameTimer;

            _server = server;
            _analyser = new GameAnalyser(this);
            _logger.LogInformation(
                "Game initialized. Mode={Mode}, Region={Region}, MasterRule={MasterRule}, StartLp={StartLp}, StartHand={StartHand}, DrawCount={DrawCount}, Timer={Timer}.",
                Mode,
                Region,
                MasterRule,
                StartLp,
                StartHand,
                DrawCount,
                Timer);
        }

        public void SetRules(BinaryReader packet)
        {
            ApplyHostInfo(HostInfo.ReadFrom(packet));
            // C++ 填充（padding）：5 bytes + 3 bytes = 8 bytes
        }

        public void SetRules(ref PacketReader packet)
        {
            ApplyHostInfo(HostInfo.ReadFrom(ref packet));
        }

        private void ApplyHostInfo(HostInfo hostInfo)
        {
            if (BanlistManager.Banlists.Count > 0)
                Banlist = BanlistManager.Banlists[BanlistManager.GetIndex(hostInfo.LfList)];
            Region = hostInfo.Region;
            MasterRule = hostInfo.MasterRule;
            Mode = hostInfo.Mode;
            IsMatch = Mode == 1;
            IsTag = Mode == 2;
            IsReady = new bool[IsTag ? 4 : 2];
            Players = new Player[IsTag ? 4 : 2];
            EnablePriority = hostInfo.EnablePriority;
            NoCheckDeck = hostInfo.NoCheckDeck;
            NoShuffleDeck = hostInfo.NoShuffleDeck;
            LifePoints[0] = hostInfo.StartLp;
            LifePoints[1] = hostInfo.StartLp;
            StartHand = hostInfo.StartHand;
            DrawCount = hostInfo.DrawCount;
            Timer = hostInfo.TimeLimit;
        }

        public void Start()
        {
            _logger.LogInformation("Game network is ready.");
            if (OnNetworkReady != null)
            {
                OnNetworkReady(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            _logger.LogInformation("Game network is stopping.");
            if (OnNetworkEnd != null)
            {
                OnNetworkEnd(this, EventArgs.Empty);
            }
        }
        
        public void SendToAll(BinaryWriter packet)
        {
            SendToPlayers(packet);
            SendToObservers(packet);
        }

        public void SendToAllBut(BinaryWriter packet, Player except)
        {
            foreach (Player player in Players)
                if (player != null && !player.Equals(except))
                    player.Send(packet);
            foreach (Player player in Observers)
                if (!player.Equals(except))
                    player.Send(packet);
        }

        public void SendToAllBut(BinaryWriter packet, int except)
        {
            if(except < CurPlayers.Length)
                SendToAllBut(packet, CurPlayers[except]);
            else
                SendToAll(packet);
        }

        public void SendToPlayers(BinaryWriter packet)
        {
            foreach (Player player in Players)
                if (player != null)
                    player.Send(packet);
        }

        public void SendToObservers(BinaryWriter packet)
        {
            foreach (Player player in Observers)
                player.Send(packet);
        }

        public void SendToTeam(BinaryWriter packet, int team)
        {
            if (!IsTag)
                Players[team].Send(packet);
            else if (team == 0)
            {
                Players[0].Send(packet);
                Players[1].Send(packet);
            }
            else
            {
                Players[2].Send(packet);
                Players[3].Send(packet);
            }
        }

        public void AddPlayer(Player player)
        {
            if (State != GameState.Lobby)
            {
                player.Type = (int)PlayerType.Observer;
                if (State != GameState.End)
                {
                    SendJoinGame(player);
                    player.SendTypeChange();
                    player.Send(GamePacketFactory.Create(StocMessage.DuelStart));
                    Observers.Add(player);
                    if (State == GameState.Duel)
                        InitNewSpectator(player);
                }
                if (OnPlayerJoin != null)
                {
                    OnPlayerJoin(this, new PlayerEventArgs(player));
                }
                _logger.LogInformation("Player {PlayerName} joined as observer while game state is {GameState}.", player.Name, State);
                return;
            }

            if (HostPlayer == null)
                HostPlayer = player;

            int pos = GetAvailablePlayerPos();
            if (pos != -1)
            {
                BinaryWriter enter = GamePacketFactory.Create(StocMessage.HsPlayerEnter);
                enter.WriteUnicode(player.Name, 20);
                enter.Write((byte)pos);
                // 填充（padding）
                enter.Write((byte)0);
                SendToAll(enter);

                Players[pos] = player;
                IsReady[pos] = false;
                player.Type = pos;
                _logger.LogInformation("Player {PlayerName} joined player slot {PlayerSlot}.", player.Name, pos);
            }
            else
            {
                BinaryWriter watch = GamePacketFactory.Create(StocMessage.HsWatchChange);
                watch.Write((short)(Observers.Count + 1));
                SendToAll(watch);

                player.Type = (int)PlayerType.Observer;
                Observers.Add(player);
                _logger.LogWarning("No player slot available for {PlayerName}; joined as observer. ObserverCount={ObserverCount}.", player.Name, Observers.Count);
            }

            SendJoinGame(player);
            player.SendTypeChange();

            for (int i = 0; i < Players.Length; i++)
            {
                if (Players[i] != null)
                {
                    BinaryWriter enter = GamePacketFactory.Create(StocMessage.HsPlayerEnter);
                    enter.WriteUnicode(Players[i].Name, 20);
                    enter.Write((byte)i);
                    // 填充（padding）
                    enter.Write((byte)0);
                    player.Send(enter);

                    if (IsReady[i])
                    {
                        BinaryWriter change = GamePacketFactory.Create(StocMessage.HsPlayerChange);
                        change.Write((byte)((i << 4) + (int)PlayerChange.Ready));
                        player.Send(change);
                    }
                }
            }

            if (Observers.Count > 0)
            {
                BinaryWriter nwatch = GamePacketFactory.Create(StocMessage.HsWatchChange);
                nwatch.Write((short)Observers.Count);
                player.Send(nwatch);
            }

            if (OnPlayerJoin != null)
            {
                OnPlayerJoin(this, new PlayerEventArgs(player));
            }
        }

        public void RemovePlayer(Player player)
        {
            _logger.LogInformation("Removing player {PlayerName} from type {PlayerType} while game state is {GameState}.", player.Name, player.Type, State);
            if (player.Equals(HostPlayer) && State == GameState.Lobby)
            {
                _logger.LogInformation("Host player {PlayerName} left lobby; stopping server.", player.Name);
                _server.Stop();
                return;
            }

            if (player.Type == (int)PlayerType.Observer)
            {
                Observers.Remove(player);
                if (State == GameState.Lobby)
                {
                    BinaryWriter nwatch = GamePacketFactory.Create(StocMessage.HsWatchChange);
                    nwatch.Write((short) Observers.Count);
                    SendToAll(nwatch);
                }
                player.Disconnect();
            }
            else if (State == GameState.Lobby)
            {
                Players[player.Type] = null!;
                IsReady[player.Type] = false;
                BinaryWriter change = GamePacketFactory.Create(StocMessage.HsPlayerChange);
                change.Write((byte)((player.Type << 4) + (int) PlayerChange.Leave));
                SendToAll(change);
                player.Disconnect();
            }
            else
                Surrender(player, 4, true);

            if (OnPlayerLeave != null)
            {
                OnPlayerLeave(this, new PlayerEventArgs(player));
            }
        }

        public void MoveToDuelist(Player player)
        {
            if (State != GameState.Lobby)
            {
                _logger.LogDebug("Ignoring MoveToDuelist for {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            int pos = GetAvailablePlayerPos();
            if (pos == -1)
            {
                _logger.LogWarning("No duelist slot available for {PlayerName}.", player.Name);
                return;
            }

            int oldType = player.Type;

            if (player.Type != (int)PlayerType.Observer)
            {
                if (!IsTag || IsReady[player.Type])
                    return;

                pos = (player.Type + 1) % 4;
                while (Players[pos] != null)
                    pos = (pos + 1) % 4;

                BinaryWriter change = GamePacketFactory.Create(StocMessage.HsPlayerChange);
                change.Write((byte)((player.Type << 4) + pos));
                SendToAll(change);

                Players[player.Type] = null!;
                Players[pos] = player;
                player.Type = pos;
                player.SendTypeChange();
            }
            else
            {
                Observers.Remove(player);
                Players[pos] = player;
                player.Type = pos;

                BinaryWriter enter = GamePacketFactory.Create(StocMessage.HsPlayerEnter);
                enter.WriteUnicode(player.Name, 20);
                enter.Write((byte)pos);
                // 填充（padding）
                enter.Write((byte)0);
                SendToAll(enter);

                BinaryWriter nwatch = GamePacketFactory.Create(StocMessage.HsWatchChange);
                nwatch.Write((short)Observers.Count);
                SendToAll(nwatch);

                player.SendTypeChange();
            }
            if (OnPlayerMove != null)
            {
                OnPlayerMove(this, new PlayerMoveEventArgs(player, oldType));
            }
            _logger.LogInformation("Player {PlayerName} moved from {OldType} to duelist slot {PlayerType}.", player.Name, oldType, player.Type);
        }

        public void MoveToObserver(Player player)
        {
            if (State != GameState.Lobby)
            {
                _logger.LogDebug("Ignoring MoveToObserver for {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            if (player.Type == (int)PlayerType.Observer)
                return;
            if (IsReady[player.Type])
            {
                _logger.LogDebug("Ignoring MoveToObserver for ready player {PlayerName}.", player.Name);
                return;
            }

            int oldType = player.Type;

            Players[player.Type] = null!;
            IsReady[player.Type] = false;
            Observers.Add(player);

            BinaryWriter change = GamePacketFactory.Create(StocMessage.HsPlayerChange);
            change.Write((byte)((player.Type << 4) + (int)PlayerChange.Observe));
            SendToAll(change);

            player.Type = (int)PlayerType.Observer;
            player.SendTypeChange();

            if (OnPlayerMove != null)
            {
                OnPlayerMove(this, new PlayerMoveEventArgs(player, oldType));
            }
            _logger.LogInformation("Player {PlayerName} moved from {OldType} to observer. ObserverCount={ObserverCount}.", player.Name, oldType, Observers.Count);
        }

        public void Chat(Player player, string msg)
        {
            BinaryWriter packet = GamePacketFactory.Create(StocMessage.Chat);
            packet.Write((short)player.Type);
            if (player.Type == (int)PlayerType.Observer)
            {
                string fullmsg = "[" + player.Name + "]: " + msg;
                CustomMessage(player, fullmsg);
            }
            else
            {
                packet.WriteUnicode(msg, msg.Length + 1);
                SendToAllBut(packet, player);
            }
            if (OnPlayerChat != null)
            {
                OnPlayerChat(this, new PlayerChatEventArgs(player, msg));
            }
        }

        public void CustomMessage(Player player, string msg)
        {
            string finalmsg = msg;
            BinaryWriter packet = GamePacketFactory.Create(StocMessage.Chat);
            packet.Write((short)PlayerType.Yellow);
            packet.WriteUnicode(finalmsg, finalmsg.Length + 1);
            SendToAllBut(packet, player);
        }

        public void SetReady(Player player, bool ready)
        {
            if (State != GameState.Lobby)
            {
                _logger.LogDebug("Ignoring ready change for {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            if (player.Type == (int)PlayerType.Observer)
                return;
            if (IsReady[player.Type] == ready)
                return;

            if (ready)
            {
                bool ocg = Region == 0 || Region == 2;
                bool tcg = Region == 1 || Region == 2;
                int result = 1;
                
                if (player.Deck != null)
                {
                    result = NoCheckDeck ? 0 : player.Deck.Check(Banlist, ocg, tcg, _options.DeckRules);
                }
                if (result != 0)
                {
                    _logger.LogWarning("Deck check failed for {PlayerName}. Result={DeckCheckResult}.", player.Name, result);
                    BinaryWriter rechange = GamePacketFactory.Create(StocMessage.HsPlayerChange);
                    rechange.Write((byte)((player.Type << 4) + (int)(PlayerChange.NotReady)));
                    player.Send(rechange);
                    BinaryWriter error = GamePacketFactory.Create(StocMessage.ErrorMsg);
                    error.Write((byte)2); // ErrorMsg.DeckError
                    // C++ 填充（padding）：1 byte + 3 bytes = 4 bytes
                    for (int i = 0; i < 3; i++)
                        error.Write((byte)0);
                    error.Write(result);
                    player.Send(error);
                    return;
                }
            }

            IsReady[player.Type] = ready;

            BinaryWriter change = GamePacketFactory.Create(StocMessage.HsPlayerChange);
            change.Write((byte)((player.Type << 4) + (int)(ready ? PlayerChange.Ready : PlayerChange.NotReady)));
            SendToAll(change);

            if (OnPlayerReady != null)
            {
                OnPlayerReady(this, new PlayerEventArgs(player));
            }
            _logger.LogInformation("Player {PlayerName} readiness changed to {Ready}.", player.Name, ready);
        }

        public void KickPlayer(Player player, int pos)
        {
            if (State != GameState.Lobby)
                return;
            if (pos >= Players.Length || !player.Equals(HostPlayer) || player.Equals(Players[pos]) || Players[pos] == null)
                return;
            RemovePlayer(Players[pos]);
        }

        public void StartDuel(Player player)
        {
            if (State != GameState.Lobby)
            {
                _logger.LogDebug("Ignoring StartDuel from {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            if (!player.Equals(HostPlayer))
            {
                _logger.LogWarning("Ignoring StartDuel from non-host player {PlayerName}.", player.Name);
                return;
            }
            for (int i = 0; i < Players.Length; i++)
            {
                if (!IsReady[i])
                {
                    _logger.LogDebug("Ignoring StartDuel because slot {PlayerSlot} is not ready.", i);
                    return;
                }
                if (Players[i] == null)
                {
                    _logger.LogDebug("Ignoring StartDuel because slot {PlayerSlot} is empty.", i);
                    return;
                }
            }

            State = GameState.Hand;
            _logger.LogInformation("Game starting. Mode={Mode}, IsTag={IsTag}, IsMatch={IsMatch}.", Mode, IsTag, IsMatch);
            SendToAll(GamePacketFactory.Create(StocMessage.DuelStart));

            SendHand();

            if (OnGameStart != null)
            {
                OnGameStart(this, EventArgs.Empty);
            }
        }

        public void HandResult(Player player, int result)
        {
            if (State != GameState.Hand)
            {
                _logger.LogDebug("Ignoring hand result from {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            if (player.Type == (int)PlayerType.Observer)
                return;
            if (result < 1 || result > 3)
                return;
            if (IsTag && player.Type != 0 && player.Type != 2)
                return;
            int type = player.Type;
            if (IsTag && player.Type == 2)
                type = 1;
            if (_handResult[type] != 0)
                return;
            _handResult[type] = result;
            _logger.LogInformation("Player {PlayerName} submitted hand result {HandResult}.", player.Name, result);
            if (_handResult[0] != 0 && _handResult[1] != 0)
            {
                BinaryWriter packet = GamePacketFactory.Create(StocMessage.HandResult);
                packet.Write((byte)_handResult[0]);
                packet.Write((byte)_handResult[1]);
                SendToTeam(packet, 0);
                SendToObservers(packet);

                packet = GamePacketFactory.Create(StocMessage.HandResult);
                packet.Write((byte)_handResult[1]);
                packet.Write((byte)_handResult[0]);
                SendToTeam(packet, 1);

                if (_handResult[0] == _handResult[1])
                {
                    _handResult[0] = 0;
                    _handResult[1] = 0;
                    SendHand();
                    return;
                }
                if ((_handResult[0] == 1 && _handResult[1] == 2) ||
                    (_handResult[0] == 2 && _handResult[1] == 3) ||
                    (_handResult[0] == 3 && _handResult[1] == 1))
                    _startplayer = IsTag ? 2 : 1;
                else
                    _startplayer = 0;
                State = GameState.Starting;
                Players[_startplayer].Send(GamePacketFactory.Create(StocMessage.SelectTp));
                TpTimer = DateTime.UtcNow;
                _logger.LogInformation("Hand result decided start player slot {StartPlayer}.", _startplayer);
            }
        }

        public void TpResult(Player player, bool result)
        {
            if (State != GameState.Starting)
            {
                _logger.LogDebug("Ignoring TP result from {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }
            if (player.Type != _startplayer)
            {
                _logger.LogDebug("Ignoring TP result from {PlayerName}: expected player slot {StartPlayer}.", player.Name, _startplayer);
                return;
            }
            
            int opt = MasterRule << 16;
            if (EnablePriority)
                opt += 0x08;
            if (NoShuffleDeck)
                opt += 0x10;
            if (IsTag)
                opt += 0x20;
            
            if (result && player.Type == (IsTag ? 2 : 1) || !result && player.Type == 0)
            {
                opt += 0x80;
            }

            CurPlayers[0] = Players[0];
            CurPlayers[1] = Players[IsTag ? 2 : 1];

            State = GameState.Duel;
            int seed = Environment.TickCount;
            if (_duelFactory is null)
            {
                _logger.LogError("Cannot start duel because no duel factory is configured.");
                throw new InvalidOperationException("A duel factory is required to start a duel.");
            }

            // 从这里开始 Game 只与 IDuelSession 通信；NativeApi 负责 ocgapi.h binding 和 handle 安全。
            _duel = _duelFactory.Create((uint)seed);
            _logger.LogInformation("Duel created. Seed={Seed}, Options={Options}, StartPlayer={StartPlayer}.", seed, opt, _startplayer);
            Random rand = new Random(seed);

            _duel.SetAnalyzer(_analyser.Analyse);
            _duel.SetErrorHandler(HandleError);

            _duel.InitPlayers(StartLp, StartHand, DrawCount);

            Replay = new Replay((uint)seed, IsTag);
            Replay.Writer.WriteUnicode(Players[0].Name, 20);
            Replay.Writer.WriteUnicode(Players[1].Name, 20);
            if (IsTag)
            {
                Replay.Writer.WriteUnicode(Players[2].Name, 20);
                Replay.Writer.WriteUnicode(Players[3].Name, 20);
            }
            Replay.Writer.Write(StartLp);
            Replay.Writer.Write(StartHand);
            Replay.Writer.Write(DrawCount);
            Replay.Writer.Write(opt);

            for (int i = 0; i < Players.Length; i++)
            {
                Player dplayer = Players[i];
                int pid = i;
                if (IsTag)
                    pid = i >= 2 ? 1 : 0;
                if (!NoShuffleDeck)
                {
                    List<int> cards = ShuffleCards(rand, dplayer.Deck.Main);
                    Replay.Writer.Write(cards.Count);
                    foreach (int id in cards)
                    {
                        if (IsTag && (i == 1 || i == 3))
                            _duel.AddTagCard(id, pid, CardLocation.Deck);
                        else
                            _duel.AddCard(id, pid, CardLocation.Deck);
                        Replay.Writer.Write(id);
                    }
                }
                else
                {
                    Replay.Writer.Write(dplayer.Deck.Main.Count);
                    for (int j = dplayer.Deck.Main.Count - 1; j >= 0; j--)
                    {
                        int id = dplayer.Deck.Main[j];
                        if (IsTag && (i == 1 || i == 3))
                            _duel.AddTagCard(id, pid, CardLocation.Deck);
                        else
                            _duel.AddCard(id, pid, CardLocation.Deck);
                        Replay.Writer.Write(id);
                    }
                }
                Replay.Writer.Write(dplayer.Deck.Extra.Count);
                foreach (int id in dplayer.Deck.Extra)
                {
                    if (IsTag && (i == 1 || i == 3))
                        _duel.AddTagCard(id, pid, CardLocation.Extra);
                    else
                        _duel.AddCard(id, pid, CardLocation.Extra);
                    Replay.Writer.Write(id);
                }
            }

            BinaryWriter packet = GamePacketFactory.Create(GameMessage.Start);
            packet.Write((byte)0);
            packet.Write((byte)MasterRule);
            packet.Write(StartLp);
            packet.Write(StartLp);
            packet.Write((short)_duel.QueryFieldCount(0, CardLocation.Deck));
            packet.Write((short)_duel.QueryFieldCount(0, CardLocation.Extra));
            packet.Write((short)_duel.QueryFieldCount(1, CardLocation.Deck));
            packet.Write((short)_duel.QueryFieldCount(1, CardLocation.Extra));
            SendToTeam(packet, 0);

            packet.BaseStream.Position = 2;
            packet.Write((byte)1);
            SendToTeam(packet, 1);

            packet.BaseStream.Position = 2;
            packet.Write((byte)0x10);
            SendToObservers(packet);

            RefreshExtra(0);
            RefreshExtra(1);

            _duel.Start(opt);
            _logger.LogInformation("Duel started. Team0Deck={Team0DeckCount}, Team1Deck={Team1DeckCount}.",
                _duel.QueryFieldCount(0, CardLocation.Deck),
                _duel.QueryFieldCount(1, CardLocation.Deck));

            TurnCount = 0;
            LifePoints[0] = StartLp;
            LifePoints[1] = StartLp;
            TimeReset();

            Process();
        }

        public void Surrender(Player player, int reason, bool force = false)
        {
            if (State == GameState.End)
                return;
            if (!force && State != GameState.Duel)
            {
                _logger.LogDebug("Ignoring surrender from {PlayerName}: game state is {GameState}, force={Force}.", player.Name, State, force);
                return;
            }
            if (player.Type == (int)PlayerType.Observer)
                return;
            _logger.LogInformation("Player {PlayerName} surrendered. Reason={Reason}, Force={Force}.", player.Name, reason, force);
            BinaryWriter win = GamePacketFactory.Create(GameMessage.Win);
            int team = player.Type;
            if (IsTag)
                team = player.Type >= 2 ? 1 : 0;
            else if (State == GameState.Hand)
                team = 1 - team;
            win.Write((byte)(1 - team));
            win.Write((byte)reason);
            SendToAll(win);

            MatchSaveResult(1 - team, reason);

            EndDuel(reason == 4);
        }

        public void RefreshAll()
        {
            RefreshMonsters(0);
            RefreshMonsters(1);
            RefreshSpells(0);
            RefreshSpells(1);
            RefreshHand(0);
            RefreshHand(1);
        }

        public void RequestField(Player player)
        {
            if (State != GameState.Duel)
            {
                _logger.LogDebug("Ignoring field request from {PlayerName}: game state is {GameState}.", player.Name, State);
                return;
            }

            try
            {
                _logger.LogDebug("Synchronizing current field for {PlayerName}.", player.Name);
                InitNewSpectator(player);
                player.Send(GamePacketFactory.CreateFieldFinish());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to synchronize current field for {PlayerName}.", player.Name);
                throw;
            }
        }

        public void RefreshAllObserver(Player observer)
        {
            RefreshMonsters(0, observer);
            RefreshMonsters(1, observer);
            RefreshSpells(0, observer);
            RefreshSpells(1, observer);
            RefreshHand(0, observer);
            RefreshHand(1, observer);
            RefreshGrave(0, observer);
            RefreshGrave(1, observer);
            RefreshExtra(0, observer);
            RefreshExtra(1, observer);
            RefreshRemoved(0, observer);
            RefreshRemoved(1, observer);
        }

        public void RefreshMonsters(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.MonsterZone, observer);
        }

        public void RefreshSpells(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.SpellZone, observer);
        }

        public void RefreshHand(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.Hand, observer);
        }

        public void RefreshGrave(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.Grave, observer);
        }

        public void RefreshRemoved(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.Removed, observer);
        }

        public void RefreshExtra(int player, Player? observer = null)
        {
            RefreshLocation(player, CardLocation.Extra, observer);
        }

        private void RefreshLocation(int player, CardLocation location, Player? observer)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(OcgCoreConstants.MaxQueryResultLength);
            try
            {
                int length = _duel.QueryFieldCard(player, location, buffer);
                SendToCorrectDestination(player, location, buffer.AsSpan(0, length), observer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh location {Location} for player {Player}.", location, player);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void SendToCorrectDestination(int player, CardLocation location, ReadOnlySpan<byte> result, Player? observer)
        {
            BinaryWriter update;

            if (observer == null)
            {
                update = GamePacketFactory.Create(GameMessage.UpdateData);
                update.Write((byte)player);
                update.Write((byte)location);
                update.Write(result);
                SendToTeam(update, player);
            }

            update = GamePacketFactory.Create(GameMessage.UpdateData);
            update.Write((byte)player);
            update.Write((byte)location);
            WritePublicCards(update, result);

            if (observer == null)
            {
                SendToTeam(update, 1 - player);
                SendToObservers(update);
            }
            else
            {
                observer.Send(update);
            }
        }

        private void WritePublicCards(BinaryWriter update, ReadOnlySpan<byte> result)
        {
            int offset = 0;
            while (offset < result.Length)
            {
                if (result.Length - offset < sizeof(int))
                    throw new EndOfStreamException("Card query result ended before a card length could be read.");

                int len = BinaryPrimitives.ReadInt32LittleEndian(result.Slice(offset, sizeof(int)));
                offset += sizeof(int);

                if (len == 4)
                {
                    update.Write(4);
                    continue;
                }

                int rawLength = len - sizeof(int);
                if (rawLength < 0 || result.Length - offset < rawLength)
                    throw new EndOfStreamException("Card query result ended before card data could be read.");

                ReadOnlySpan<byte> raw = result.Slice(offset, rawLength);
                offset += rawLength;

                bool isFaceup = (raw[11] & (int)CardPosition.FaceUp) != 0;
                if (isFaceup)
                {
                    update.Write(len);
                    update.Write(raw);
                }
                else
                {
                    update.Write(8);
                    update.Write(0);
                }
            }
        }

        public void RefreshSingle(int player, int location, int sequence)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(OcgCoreConstants.MaxQueryResultLength);
            try
            {
                int length = _duel.QueryCard(player, location, sequence, buffer);
                ReadOnlySpan<byte> result = buffer.AsSpan(0, length);

                if (location == (int)CardLocation.Removed && (result[15] & (int)CardPosition.FaceDown) != 0)
                    return;

                BinaryWriter update = GamePacketFactory.Create(GameMessage.UpdateCard);
                update.Write((byte)player);
                update.Write((byte)location);
                update.Write((byte)sequence);
                update.Write(result);
                CurPlayers[player].Send(update);

                if (IsTag)
                {
                    if ((location & (int)CardLocation.Onfield) != 0)
                    {
                        SendToTeam(update, player);
                        if ((result[15] & (int)CardPosition.FaceUp) != 0)
                            SendToTeam(update, 1 - player);
                    }
                    else
                    {
                        CurPlayers[player].Send(update);
                        if ((location & 0x90) != 0)
                            SendToAllBut(update, player);
                    }
                }
                else
                {
                    if ((location & 0x90) != 0 || ((location & 0x2c) != 0 && (result[15] & (int)CardPosition.FaceUp) != 0))
                        SendToAllBut(update, player);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh card at player={Player}, location={Location}, sequence={Sequence}.", player, location, sequence);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public int WaitForResponse()
        {
            WaitForResponse(_lastresponse);
            return _lastresponse;
        }

        public void WaitForResponse(int player)
        {
            _lastresponse = player;
            CurPlayers[player].State = PlayerState.Response;
            _logger.LogDebug("Waiting for response from player slot {Player}.", player);
            SendToAllBut(GamePacketFactory.Create(GameMessage.Waiting), player);
            TimeStart();
            BinaryWriter packet = GamePacketFactory.Create(StocMessage.TimeLimit);
            packet.Write((byte)player);
            packet.Write((byte)0); // C++ padding
            packet.Write((short)_timelimit[player]);
            SendToPlayers(packet);
        }

        public void SetResponse(int resp)
        {
            if (!Replay.Disabled)
            {
                Replay.Writer.Write((byte)4);
                Replay.Writer.Write(BitConverter.GetBytes(resp));
                Replay.Check();
            }

            TimeStop();
            _logger.LogDebug("Setting integer response for player slot {Player}.", _lastresponse);
            _duel.SetResponse(resp);
        }

        public void SetResponse(ReadOnlySpan<byte> resp)
        {
            if (!Replay.Disabled)
            {
                Replay.Writer.Write((byte)resp.Length);
                Replay.Writer.Write(resp);
                Replay.Check();
            }

            TimeStop();
            _logger.LogDebug("Setting binary response for player slot {Player}. Length={ResponseLength}.", _lastresponse, resp.Length);
            _duel.SetResponse(resp);
            Process();
        }

        public void EndDuel(bool force)
        {
            if (State == GameState.End)
            {
                return;
            }

            if (State == GameState.Duel)
            {
                _logger.LogInformation("Ending duel. Force={Force}.", force);
                if (!Replay.Disabled)
                {
                    Replay.End();
                    byte[] replayData = Replay.GetContent();
                    BinaryWriter packet = GamePacketFactory.Create(StocMessage.Replay);
                    packet.Write(replayData);
                    SendToAll(packet);
                }

                _duel.End();
            }

            if (IsMatch && !force && !MatchIsEnd())
            {
                IsReady[0] = false;
                IsReady[1] = false;
                State = GameState.Side;
                SideTimer = DateTime.UtcNow;
                _logger.LogInformation("Entering side state for match. DuelCount={DuelCount}.", DuelCount);
                SendToPlayers(GamePacketFactory.CreateDeckCount(
                    Players[0]?.Deck?.Main.Count ?? 0,
                    Players[0]?.Deck?.Extra.Count ?? 0,
                    Players[0]?.Deck?.Side.Count ?? 0,
                    Players[1]?.Deck?.Main.Count ?? 0,
                    Players[1]?.Deck?.Extra.Count ?? 0,
                    Players[1]?.Deck?.Side.Count ?? 0));
                SendToPlayers(GamePacketFactory.Create(StocMessage.ChangeSide));
                SendToObservers(GamePacketFactory.Create(StocMessage.WaitingSide));
            }
            else
            {
                CalculateWinner();
                End();
            }
        }

        public void End()
        {
            State = GameState.End;
            _logger.LogInformation("Game ended. Winner={Winner}, DuelCount={DuelCount}.", Winner, DuelCount);

            SendToAll(GamePacketFactory.Create(StocMessage.DuelEnd));
            _server.StopDelayed();

            if (OnGameEnd != null)
            {
                OnGameEnd(this, EventArgs.Empty);
            }
        }

        public void TimeReset()
        {
            _timelimit[0] = Timer;
            _timelimit[1] = Timer;
        }

        public void TimeStart()
        {
            _time = DateTime.UtcNow;
        }

        public void TimeStop()
        {
            if (_time != null)
            {
                TimeSpan elapsed = DateTime.UtcNow - _time.Value;
                _timelimit[_lastresponse] -= (int)elapsed.TotalSeconds;
                if (_timelimit[_lastresponse] < 0)
                    _timelimit[_lastresponse] = 0;
                _time = null;
            }
        }

        public void TimeTick()
        {
            if (State == GameState.Duel)
            {
                if (_time != null)
                {
                    TimeSpan elapsed = DateTime.UtcNow - _time.Value;
                    if ((int)elapsed.TotalSeconds > _timelimit[_lastresponse])
                    {
                        _logger.LogWarning("Player slot {Player} timed out during duel.", _lastresponse);
                        Surrender(CurPlayers[_lastresponse], 3);
                    }
                }
            }

            if (State == GameState.Side)
            {
                TimeSpan elapsed = DateTime.UtcNow - SideTimer;

                if (elapsed.TotalMilliseconds >= 120000)
                {
                    _logger.LogWarning("Side timer expired.");
                    if (!IsReady[0] && !IsReady[1])
                    {
                        EndDuel(true);
                        return;
                    }

                    Surrender(!IsReady[0] ? Players[0] : Players[1], 3, true);
                }
            }

            if (State == GameState.Starting)
            {
                if (IsTpSelect)
                {
                    TimeSpan elapsed = DateTime.UtcNow - TpTimer;

                    if (elapsed.TotalMilliseconds >= 30000)
                    {
                        _logger.LogWarning("TP selection timer expired for player slot {Player}.", _startplayer);
                        Surrender(CurPlayers[_startplayer], 3, true);
                    }

                }
            }
            if (State == GameState.Hand)
            {
                TimeSpan elapsed = DateTime.UtcNow - RpsTimer;

                if ((int)elapsed.TotalMilliseconds >= 60000)
                {
                    _logger.LogWarning("Hand selection timer expired.");
                    if (_handResult[0] != 0)
                        Surrender(Players[IsTag ? 2 : 1], 3, true);
                    else if (_handResult[1] != 0)
                        Surrender(Players[0], 3, true);
                    else
                        EndDuel(true);
                }
            }
        }

        public void MatchSaveResult(int player, int reason)
        {
            if (player < 2)
                _startplayer = 1 - player;
            else
                _startplayer = 1 - _startplayer;
            MatchResults[DuelCount] = player;
            MatchReasons[DuelCount++] = reason;
            _logger.LogInformation("Duel result saved. Winner={Winner}, Reason={Reason}, DuelCount={DuelCount}.", player, reason, DuelCount);
            
            if (OnDuelEnd != null)
            {
                OnDuelEnd(this, EventArgs.Empty);
            }
        }

        public void MatchKill()
        {
            _matchKill = true;
        }

        public bool MatchIsEnd()
        {
            if (_matchKill)
                return true;
            int[] wins = new int[3];
            for (int i = 0; i < DuelCount; i++)
                wins[MatchResults[i]]++;
            return wins[0] == 2 || wins[1] == 2 || wins[0] + wins[1] + wins[2] == 3;
        }

        public void MatchSide()
        {
            if (IsReady[0] && IsReady[1])
            {
                State = GameState.Starting;
                IsTpSelect = true;
                TpTimer = DateTime.UtcNow;
                TimeReset();
                _logger.LogInformation("Both players are ready after side. Selecting TP with start player {StartPlayer}.", _startplayer);
                Players[_startplayer].Send(GamePacketFactory.Create(StocMessage.SelectTp));
            }
        }

        private int GetAvailablePlayerPos()
        {
            for (int i = 0; i < Players.Length; i++)
            {
                if (Players[i] == null)
                    return i;
            }
            return -1;
        }

        private void SendHand()
        {
            RpsTimer = DateTime.UtcNow;
            BinaryWriter hand = GamePacketFactory.Create(StocMessage.SelectHand);
            if (IsTag)
            {
                Players[0].Send(hand);
                Players[2].Send(hand);
            }
            else
                SendToPlayers(hand);
        }

        private void Process()
        {
            int result;
            try
            {
                result = _duel.Process();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Native duel process failed.");
                throw;
            }

            _logger.LogTrace("Native duel process returned {ProcessResult}.", result);
            switch (result)
            {
                case -1:
                    _logger.LogInformation("Native duel requested forced end.");
                    EndDuel(true);
                    break;
                case 2: // Game finished
                    _logger.LogInformation("Native duel finished.");
                    EndDuel(false);
                    break;
            }
        }

        private void SendJoinGame(Player player)
        {
            BinaryWriter join = GamePacketFactory.Create(StocMessage.JoinGame);
            join.Write(Banlist == null ? 0U : Banlist.Hash);
            join.Write((byte)Region);
            join.Write((byte)Mode);
            join.Write((byte)MasterRule);
            join.Write(NoCheckDeck);
            join.Write(NoShuffleDeck);
            // C++ 填充（padding）：5 bytes + 3 bytes = 8 bytes
            for (int i = 0; i < 3; i++)
                join.Write((byte)0);
            join.Write(StartLp);
            join.Write((byte)StartHand);
            join.Write((byte)DrawCount);
            join.Write((short)Timer);
            player.Send(join);

            if (State != GameState.Lobby)
                SendDuelingPlayers(player);
        }

        private void SendDuelingPlayers(Player player)
        {
            for (int i = 0; i < Players.Length; i++)
            {
                BinaryWriter enter = GamePacketFactory.Create(StocMessage.HsPlayerEnter);
                enter.WriteUnicode(Players[i].Name, 20);
                enter.Write((byte)i);
                // 填充（padding）
                enter.Write((byte)0);
                player.Send(enter);
            }
        }

        private void InitNewSpectator(Player player)
        {
            BinaryWriter packet = GamePacketFactory.Create(GameMessage.Start);
            packet.Write((byte)0x10);
            packet.Write((byte)MasterRule);
            packet.Write(LifePoints[0]);
            packet.Write(LifePoints[1]);
            packet.Write((short)0); // deck
            packet.Write((short)0); // extra
            packet.Write((short)0); // deck
            packet.Write((short)0);  // extra
            player.Send(packet);
            
            BinaryWriter turn = GamePacketFactory.Create(GameMessage.NewTurn);
            turn.Write((byte)0);
            player.Send(turn);
            if (CurrentPlayer == 1)
            {
                turn = GamePacketFactory.Create(GameMessage.NewTurn);
                turn.Write((byte)0);
                player.Send(turn);
            }

            BinaryWriter reload = GamePacketFactory.Create(GameMessage.ReloadField);
            byte[] fieldInfo = ArrayPool<byte>.Shared.Rent(OcgCoreConstants.FieldInfoLength);
            try
            {
                int fieldInfoLength = _duel.QueryFieldInfo(fieldInfo);
                reload.Write(fieldInfo.AsSpan(1, fieldInfoLength - 1));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(fieldInfo);
            }
            player.Send(reload);

            RefreshAllObserver(player);
        }
        
        private void HandleError(string error)
        {
            _logger.LogError("Native/Lua error: {NativeError}", error);
            BinaryWriter packet = GamePacketFactory.Create(StocMessage.Chat);
            packet.Write((short)PlayerType.Observer);
            packet.WriteUnicode(error, error.Length + 1);
            SendToAll(packet);

            string errorFile = "lua_" + DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt";
            File.WriteAllText(errorFile, error);
            _logger.LogError("Native/Lua error details were written to {ErrorFile}.", errorFile);
        }

        private static List<int> ShuffleCards(Random rand, IEnumerable<int> cards)
        {
            List<int> shuffled = new List<int>(cards);
            for (int i = shuffled.Count-1 ; i > 0; --i)
            {
                int pos = rand.Next(i+1);
                int tmp = shuffled[i];
                shuffled[i] = shuffled[pos];
                shuffled[pos] = tmp;
            }
            return shuffled;
        }

        private void CalculateWinner()
        {
            int winner = -1;
            if (DuelCount > 0)
            {
                if (!_matchKill && DuelCount != 1)
                {
                    int[] wins = new int[3];
                    for (int i = 0; i < DuelCount; i++)
                        wins[MatchResults[i]]++;
                    if (wins[0] > wins[1])
                        winner = 0;
                    else if (wins[1] > wins[0])
                        winner = 1;
                    else
                        winner = 2;
                }
                else
                    winner = MatchResults[DuelCount - 1];
            }

            Winner = winner;
        }

    }
}
