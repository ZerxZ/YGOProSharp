using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 持有监听 socket、已连接 client 与房间实例（支持单房间兼容模式与多房间路由模式）。
    /// </summary>
    public class CoreServer
    {
        public const int DefaultPort = 7911;

        public bool IsRunning { get; private set; }
        public bool IsListening { get; private set; }
        public AddonsManager? Addons { get; private set; }
        public Game? Game { get; private set; }
        public bool IsMultiRoom => _options.EnableMultiRoom;
        public int ClientCount
        {
            get
            {
                lock (_clientsLock)
                    return _clients.Count;
            }
        }

        public int RoomCount
        {
            get
            {
                if (!_options.EnableMultiRoom)
                    return Game is null ? 0 : 1;

                lock (_roomsLock)
                    return _rooms.Count;
            }
        }

        public event Action<Game>? OnRoomCreated;
        public event Action<Game>? OnRoomRemoved;

        private NetworkServer? _listener;
        private readonly IDuelFactory? _duelFactory;
        private readonly ILogger<CoreServer> _logger;
        private readonly ICardRepository _cardRepository;
        private readonly ServerOptions _options;
        private readonly RoomPasswordParser _passwordParser;
        private readonly IDisposable? _ownedResource;
        private readonly object _clientsLock = new();
        private readonly List<YGOClient> _clients = new();
        private readonly List<YGOClient> _tickClients = new();
        private readonly List<YGOClient> _disconnectedClients = new();

        private readonly object _roomsLock = new();
        private readonly Dictionary<string, ManagedRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
        private int _roomSequence;

        private bool _closePending;

        /// <summary>
        /// 使用可选业务依赖创建 server，同时通过 <see cref="AppLog"/> 保持全局日志入口。
        /// </summary>
        public CoreServer(IDuelFactory? duelFactory = null, ICardRepository? cardRepository = null, ServerOptions? options = null)
            : this(duelFactory, cardRepository, options, ownedResource: null)
        {
        }

        internal CoreServer(
            IDuelFactory? duelFactory,
            ICardRepository? cardRepository,
            ServerOptions? options,
            IDisposable? ownedResource)
        {
            _duelFactory = duelFactory;
            _logger = AppLog.CreateLogger<CoreServer>();
            _cardRepository = cardRepository ?? EmptyCardRepository.Instance;
            _options = options ?? new ServerOptions();
            _passwordParser = _options.RoomPasswordParser ?? RoomPasswordParser.Default;
            _ownedResource = ownedResource;
        }

        public void Start()
        {
            StartCore(_options.EnableNetworkListener);
        }

        /// <summary>
        /// 以进程内嵌入式模式启动服务端，不打开任何 TCP 监听端口；客户端可通过
        /// <see cref="ConnectLocalClient"/> 或 <see cref="CreateLocalTransport"/> 在内存中直接连入。
        /// </summary>
        public void StartEmbedded()
        {
            StartCore(enableNetworkListener: false);
        }

        private void StartCore(bool enableNetworkListener)
        {
            if (IsRunning)
                return;

            _closePending = false;
            YGOProSharpServer.SetClientVersion(_options.ClientVersion);

            if (!_options.EnableMultiRoom)
            {
                Addons = CreateAddonsManager();
                Game = new Game(this, _duelFactory, _cardRepository, _options.Game)
                {
                    RoomName = "default",
                    IsManagedRoom = false
                };
                Addons.Init(Game);
            }
            else
            {
                Addons = null;
                Game = null;
            }

            if (!enableNetworkListener)
            {
                IsRunning = true;
                IsListening = false;
                Game?.Start();
                _logger.LogInformation("Core server started in embedded mode without TCP listener. MultiRoom={MultiRoom}.", _options.EnableMultiRoom);
                return;
            }

            try
            {
                int port = _options.Port;
                _logger.LogInformation("Starting core server on {Address}:{Port}. MultiRoom={MultiRoom}.", IPAddress.Any, port, _options.EnableMultiRoom);
                _listener = new NetworkServer(IPAddress.Any, port);
                _listener.ClientConnected += Listener_ClientConnected;
                _listener.Start();
                IsRunning = true;
                IsListening = true;
                Game?.Start();
                _logger.LogInformation("Core server started.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start core server.");
            }
        }

        public void StopListening()
        {
            if (!IsListening)
                return;
            IsListening = false;
            _logger.LogInformation("Stopping listener.");
            _listener?.Close();
        }

        public void Stop()
        {
            YGOClient[] clientsSnapshot;
            lock (_clientsLock)
            {
                clientsSnapshot = _clients.ToArray();
                _clients.Clear();
            }

            _logger.LogInformation("Stopping core server with {ClientCount} connected clients.", clientsSnapshot.Length);
            StopListening();
            foreach (YGOClient client in clientsSnapshot)
                client.Close();

            if (_options.EnableMultiRoom)
            {
                ManagedRoom[] roomsSnapshot;
                lock (_roomsLock)
                {
                    roomsSnapshot = _rooms.Values.ToArray();
                    _rooms.Clear();
                }

                for (int i = 0; i < roomsSnapshot.Length; i++)
                {
                    roomsSnapshot[i].Game.Stop();
                    roomsSnapshot[i].Addons.Dispose();
                    OnRoomRemoved?.Invoke(roomsSnapshot[i].Game);
                }
            }
            else
            {
                Game?.Stop();
                Addons?.Dispose();
            }

            _ownedResource?.Dispose();
            IsRunning = false;
            _logger.LogInformation("Core server stopped.");
        }

        public void StopDelayed()
        {
            _logger.LogInformation("Core server delayed stop requested.");
            StopListening();
            _closePending = true;
        }

        /// <summary>
        /// 获取当前所有活动房间的状态快照（单房间模式返回 0 或 1 条；多房间模式返回所有活动房间）。
        /// </summary>
        public IReadOnlyList<RoomSnapshot> GetRooms()
        {
            if (!_options.EnableMultiRoom)
                return Game is null ? [] : [Game.CreateSnapshot()];

            lock (_roomsLock)
            {
                List<RoomSnapshot> snapshots = new(_rooms.Count);
                foreach (ManagedRoom room in _rooms.Values)
                    snapshots.Add(room.Game.CreateSnapshot());
                return snapshots;
            }
        }

        /// <summary>
        /// 按房间名查询活动房间实例（多房间模式与单房间模式均可用）。
        /// </summary>
        public bool TryGetRoom(string roomName, out Game? game)
        {
            if (!_options.EnableMultiRoom)
            {
                if (Game is not null && (string.IsNullOrWhiteSpace(roomName) || string.Equals(Game.RoomName, roomName, StringComparison.OrdinalIgnoreCase)))
                {
                    game = Game;
                    return true;
                }

                game = null;
                return false;
            }

            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(roomName, out ManagedRoom? managed))
                {
                    game = managed.Game;
                    return true;
                }
            }

            game = null;
            return false;
        }

        /// <summary>
        /// 创建一个直连到当前服务端的进程内内存传输通道（客户端侧），服务端侧会自动注册为已连接玩家连接。
        /// 适合直接传入 WindBot 或自定义 C# 协议客户端，完全不经过 TCP/WebSocket 端口。
        /// </summary>
        public InMemoryNetworkTransport CreateLocalTransport(IPAddress? remoteAddress = null)
        {
            if (!IsRunning || (!_options.EnableMultiRoom && Game is null))
                throw new InvalidOperationException("Core server must be started before creating a local client connection.");

            (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
                InMemoryNetworkTransport.CreatePair(remoteAddress ?? IPAddress.Loopback, IPAddress.Loopback, connected: true);

            AddClient(new YGOClient(serverTransport));
            return clientTransport;
        }

        /// <summary>
        /// 创建一个直连到当前服务端的进程内 <see cref="YGOClient"/>。
        /// </summary>
        public YGOClient ConnectLocalClient(IPAddress? remoteAddress = null)
        {
            return new YGOClient(CreateLocalTransport(remoteAddress));
        }

        public void AddClient(YGOClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            if (!IsRunning || (!_options.EnableMultiRoom && Game is null))
                throw new InvalidOperationException("Server game has not been initialized.");

            _logger.LogInformation("Adding client {RemoteAddress}.", client.RemoteIPAddress);

            // Player 是该连接的 CTOS protocol adapter；在多房间模式下先绑定到 CoreServer，待 JoinGame/CreateGame 再路由至具体房间。
            Player player = _options.EnableMultiRoom
                ? new Player(this, client)
                : new Player(Game!, client, this);

            client.PacketReceivedRaw += packet =>
            {
                try
                {
                    player.Parse(packet.Span);
                }
                catch (Exception)
                {
                    player.Disconnect();
                }
            };
            client.Disconnected += packet => player.OnDisconnected();

            int count;
            lock (_clientsLock)
            {
                _clients.Add(client);
                count = _clients.Count;
            }

            _logger.LogDebug("Client {RemoteAddress} registered. ClientCount={ClientCount}.", client.RemoteIPAddress, count);
        }

        internal Game? ResolveGameForJoin(Player player, string rawPassword)
        {
            ParsedRoomPassword parsed = _passwordParser.Parse(rawPassword);

            if (!_options.EnableMultiRoom)
            {
                if (Game is null)
                    return null;

                if (_options.ApplyPasswordRulesInSingleRoom && Game.State == GameState.Lobby && Game.IsEmpty)
                {
                    if (parsed.HasRulePrefix && parsed.Overrides.HasAnyOverride)
                        Game.ApplyRuleOverrides(parsed.Overrides);
                    if (!string.IsNullOrWhiteSpace(parsed.RoomName))
                        Game.RoomName = parsed.RoomName;
                }

                return Game;
            }

            string roomKey = DetermineJoinRoomKey(parsed);
            Game createdOrExisting;
            bool created = false;

            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(roomKey, out ManagedRoom? existing) && !existing.PendingRemoval && existing.Game.State != GameState.End)
                {
                    return existing.Game;
                }

                if (_rooms.Count >= Math.Max(1, _options.MaxRooms))
                {
                    _logger.LogWarning("Rejected join for {PlayerName} into room '{RoomName}': max rooms limit ({MaxRooms}) reached.", player.Name, roomKey, _options.MaxRooms);
                    return null;
                }

                GameOptions roomOptions = parsed.HasRulePrefix ? parsed.ApplyTo(_options.Game) : _options.Game;
                ManagedRoom managed = CreateManagedRoom(roomKey, roomOptions);
                _rooms[roomKey] = managed;
                createdOrExisting = managed.Game;
                created = true;
            }

            if (created)
            {
                createdOrExisting.Start();
                OnRoomCreated?.Invoke(createdOrExisting);
                _logger.LogInformation("Created room '{RoomName}' via JoinGame password '{RawPassword}'. ActiveRooms={RoomCount}.", roomKey, rawPassword, RoomCount);
            }

            return createdOrExisting;
        }

        internal Game? ResolveGameForCreate(Player player, HostInfo hostInfo, string hostName, string password)
        {
            if (!_options.EnableMultiRoom)
            {
                if (Game is null)
                    return null;

                Game.SetRules(hostInfo);
                return Game;
            }

            ParsedRoomPassword parsed = _passwordParser.Parse(password);
            string roomKey = !string.IsNullOrWhiteSpace(parsed.RoomName)
                ? parsed.RoomName
                : (!string.IsNullOrWhiteSpace(hostName)
                    ? hostName.Trim()
                    : $"room-{Interlocked.Increment(ref _roomSequence)}");

            Game createdGame;
            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(roomKey, out ManagedRoom? existing) && !existing.PendingRemoval && existing.Game.State != GameState.End)
                {
                    return existing.Game;
                }

                if (_rooms.Count >= Math.Max(1, _options.MaxRooms))
                {
                    _logger.LogWarning("Rejected CreateGame for {PlayerName}: max rooms limit ({MaxRooms}) reached.", player.Name, _options.MaxRooms);
                    return null;
                }

                ManagedRoom managed = CreateManagedRoom(roomKey, _options.Game);
                managed.Game.SetRules(hostInfo);
                if (parsed.HasRulePrefix && parsed.Overrides.HasAnyOverride)
                    managed.Game.ApplyRuleOverrides(parsed.Overrides);
                _rooms[roomKey] = managed;
                createdGame = managed.Game;
            }

            createdGame.Start();
            OnRoomCreated?.Invoke(createdGame);
            _logger.LogInformation("Created room '{RoomName}' via CreateGame by {PlayerName}. ActiveRooms={RoomCount}.", roomKey, player.Name, RoomCount);
            return createdGame;
        }

        internal void CloseRoom(Game room)
        {
            if (!_options.EnableMultiRoom)
            {
                Stop();
                return;
            }

            ManagedRoom? removed = null;
            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(room.RoomName, out ManagedRoom? existing) && ReferenceEquals(existing.Game, room))
                {
                    existing.PendingRemoval = true;
                    _rooms.Remove(room.RoomName);
                    removed = existing;
                }
            }

            if (removed is null)
                return;

            List<Player> toDisconnect = new();
            foreach (Player p in room.Players)
            {
                if (p is not null)
                    toDisconnect.Add(p);
            }
            toDisconnect.AddRange(room.Observers);

            for (int i = 0; i < toDisconnect.Count; i++)
                toDisconnect[i].Disconnect();

            removed.Game.Stop();
            removed.Addons.Dispose();
            OnRoomRemoved?.Invoke(removed.Game);
            _logger.LogInformation("Closed room '{RoomName}'. ActiveRooms={RoomCount}.", room.RoomName, RoomCount);
        }

        internal void OnRoomEnded(Game room)
        {
            if (!_options.EnableMultiRoom)
            {
                StopDelayed();
                return;
            }

            lock (_roomsLock)
            {
                if (_rooms.TryGetValue(room.RoomName, out ManagedRoom? existing) && ReferenceEquals(existing.Game, room))
                    existing.PendingRemoval = true;
            }
        }

        public void Tick()
        {
            if (!IsRunning || (!_options.EnableMultiRoom && Game is null))
                return;

            _listener?.Update();

            _tickClients.Clear();
            lock (_clientsLock)
            {
                _tickClients.AddRange(_clients);
            }

            for (int i = 0; i < _tickClients.Count; i++)
            {
                YGOClient client = _tickClients[i];
                client.Update();
                if (!client.IsConnected)
                {
                    _logger.LogInformation("Removing disconnected client {RemoteAddress}.", client.RemoteIPAddress);
                    _disconnectedClients.Add(client);
                }
            }

            if (_options.EnableMultiRoom)
                TickManagedRooms();
            else
                Game!.TimeTick();

            int remainingClients;
            lock (_clientsLock)
            {
                if (_disconnectedClients.Count > 0)
                {
                    for (int i = 0; i < _disconnectedClients.Count; i++)
                        _clients.Remove(_disconnectedClients[i]);
                    _disconnectedClients.Clear();
                }

                remainingClients = _clients.Count;
            }

            if (_closePending && remainingClients == 0)
                Stop();
        }

        private void TickManagedRooms()
        {
            ManagedRoom[] snapshot;
            lock (_roomsLock)
            {
                if (_rooms.Count == 0)
                    return;
                snapshot = _rooms.Values.ToArray();
            }

            List<ManagedRoom>? toRemove = null;
            for (int i = 0; i < snapshot.Length; i++)
            {
                ManagedRoom room = snapshot[i];
                room.Game.TimeTick();

                if (room.PendingRemoval || room.Game.State == GameState.End)
                {
                    toRemove ??= new List<ManagedRoom>();
                    toRemove.Add(room);
                }
            }

            if (toRemove is null)
                return;

            for (int i = 0; i < toRemove.Count; i++)
            {
                ManagedRoom room = toRemove[i];
                bool removed;
                lock (_roomsLock)
                {
                    removed = _rooms.TryGetValue(room.Game.RoomName, out ManagedRoom? current) &&
                              ReferenceEquals(current, room) &&
                              _rooms.Remove(room.Game.RoomName);
                }

                if (removed)
                {
                    room.Game.Stop();
                    room.Addons.Dispose();
                    OnRoomRemoved?.Invoke(room.Game);
                    _logger.LogInformation("Cleaned up ended room '{RoomName}'. ActiveRooms={RoomCount}.", room.Game.RoomName, RoomCount);
                }
            }
        }

        private ManagedRoom CreateManagedRoom(string roomKey, GameOptions roomOptions)
        {
            AddonsManager addons = CreateAddonsManager();
            Game game = new(this, _duelFactory, _cardRepository, roomOptions)
            {
                RoomName = roomKey,
                IsManagedRoom = true
            };
            addons.Init(game);
            return new ManagedRoom(game, addons);
        }

        private AddonsManager CreateAddonsManager()
        {
            return new AddonsManager(
                _options.StandardStreamProtocol,
                _options.EnableChatCommands,
                _options.ChatCommands,
                _options.AddonFactories);
        }

        private static string DetermineJoinRoomKey(ParsedRoomPassword parsed)
        {
            if (!string.IsNullOrWhiteSpace(parsed.RoomName))
                return parsed.RoomName;

            if (parsed.HasRulePrefix && !string.IsNullOrWhiteSpace(parsed.RawPassword))
                return parsed.RawPassword;

            return "default";
        }

        private void Listener_ClientConnected(NetworkClient client)
        {
            _logger.LogInformation("Client connected from {RemoteAddress}.", client.RemoteIPAddress);
            AddClient(new YGOClient(client));
        }

        private sealed class ManagedRoom
        {
            public Game Game { get; }
            public AddonsManager Addons { get; }
            public bool PendingRemoval { get; set; }

            public ManagedRoom(Game game, AddonsManager addons)
            {
                Game = game;
                Addons = addons;
            }
        }
    }
}
