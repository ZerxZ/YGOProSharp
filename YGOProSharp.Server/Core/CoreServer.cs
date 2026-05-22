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

        private NetworkServer? _listener;
        private readonly IDuelFactory? _duelFactory;
        private readonly ILogger<CoreServer> _logger;
        private readonly ICardRepository _cardRepository;
        private readonly ServerOptions _options;
        private readonly List<YGOClient> _clients = new();

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
            if (IsRunning)
                return;
            Addons = new AddonsManager(_options.StandardStreamProtocol);
            Game = new Game(this, _duelFactory, _cardRepository, _options.Game);
            Addons.Init(Game);
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
            _logger.LogInformation("Stopping core server with {ClientCount} connected clients.", _clients.Count);
            StopListening();
            foreach (YGOClient client in _clients)
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

        public void AddClient(YGOClient client)
        {
            _logger.LogInformation("Adding client {RemoteAddress}.", client.RemoteIPAddress);
            _clients.Add(client);
            if (Game is null)
                throw new InvalidOperationException("Server game has not been initialized.");

            // Player 是该连接的 CTOS protocol adapter；Game 不感知 socket 细节。
            Player player = new Player(Game, client);

            client.PacketReceivedRaw += packet => player.Parse(packet.Span);
            client.Disconnected += packet => player.OnDisconnected();
            _logger.LogDebug("Client {RemoteAddress} registered. ClientCount={ClientCount}.", client.RemoteIPAddress, _clients.Count);
        }
        
        public void Tick()
        {
            if (_listener is null || Game is null)
                return;

            _listener.Update();

            List<YGOClient> disconnectedClients = new List<YGOClient>();

            foreach (YGOClient client in _clients)
            {
                client.Update();
                if (!client.IsConnected)
                {
                    _logger.LogInformation("Removing disconnected client {RemoteAddress}.", client.RemoteIPAddress);
                    disconnectedClients.Add(client);
                }
            }

            Game.TimeTick();

            while (disconnectedClients.Count > 0)
            {
                _clients.Remove(disconnectedClients[0]);
                disconnectedClients.RemoveAt(0);
            }

            if (_closePending && _clients.Count == 0)
                Stop();
        }

        private void Listener_ClientConnected(NetworkClient client)
        {
            _logger.LogInformation("Client connected from {RemoteAddress}.", client.RemoteIPAddress);
            AddClient(new YGOClient(client));
        }
    }
}
