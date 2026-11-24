using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core.Cards;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Protocol;

namespace YGOProSharp.Server
{
    /// <summary>
    /// 持有监听 socket、已连接 client 和当前活动的 <see cref="Game"/> 实例。
    /// </summary>
    public class CoreServer
    {
        public const int DefaultPort = 7911;

        public bool IsRunning { get; private set; }
        public bool IsListening { get; private set; }
        public AddonsManager? Addons { get; private set; }
        public Game? Game { get; private set; }
        public int ClientCount
        {
            get
            {
                lock (_clientsLock)
                    return _clients.Count;
            }
        }

        private NetworkServer? _listener;
        private readonly IDuelFactory? _duelFactory;
        private readonly ILogger<CoreServer> _logger;
        private readonly ICardRepository _cardRepository;
        private readonly ServerOptions _options;
        private readonly object _clientsLock = new();
        private readonly List<YGOClient> _clients = new();
        private readonly List<YGOClient> _tickClients = new();
        private readonly List<YGOClient> _disconnectedClients = new();

        private bool _closePending;

        /// <summary>
        /// 使用可选业务依赖创建 server，同时通过 <see cref="AppLog"/> 保持全局日志入口。
        /// </summary>
        public CoreServer(IDuelFactory? duelFactory = null, ICardRepository? cardRepository = null, ServerOptions? options = null)
        {
            _duelFactory = duelFactory;
            _logger = AppLog.CreateLogger<CoreServer>();
            _cardRepository = cardRepository ?? EmptyCardRepository.Instance;
            _options = options ?? new ServerOptions();
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
            Addons = new AddonsManager(_options.StandardStreamProtocol);
            Game = new Game(this, _duelFactory, _cardRepository, _options.Game);
            Addons.Init(Game);

            if (!enableNetworkListener)
            {
                IsRunning = true;
                IsListening = false;
                Game.Start();
                _logger.LogInformation("Core server started in embedded mode without TCP listener.");
                return;
            }

            try
            {
                int port = _options.Port;
                _logger.LogInformation("Starting core server on {Address}:{Port}.", IPAddress.Any, port);
                _listener = new NetworkServer(IPAddress.Any, port);
                _listener.ClientConnected += Listener_ClientConnected;
                _listener.Start();
                IsRunning = true;
                IsListening = true;
                Game.Start();
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
            Game?.Stop();
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
        /// 创建一个直连到当前服务端的进程内内存传输通道（客户端侧），服务端侧会自动注册为已连接玩家连接。
        /// 适合直接传入 WindBot 或自定义 C# 协议客户端，完全不经过 TCP/WebSocket 端口。
        /// </summary>
        public InMemoryNetworkTransport CreateLocalTransport(IPAddress? remoteAddress = null)
        {
            if (!IsRunning || Game is null)
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

            if (Game is null)
                throw new InvalidOperationException("Server game has not been initialized.");

            _logger.LogInformation("Adding client {RemoteAddress}.", client.RemoteIPAddress);

            // Player 是该连接的 CTOS protocol adapter；Game 不感知 socket 细节。
            Player player = new Player(Game, client);

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
        
        public void Tick()
        {
            if (!IsRunning || Game is null)
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

            Game.TimeTick();

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

        private void Listener_ClientConnected(NetworkClient client)
        {
            _logger.LogInformation("Client connected from {RemoteAddress}.", client.RemoteIPAddress);
            AddClient(new YGOClient(client));
        }
    }
}
