using System;
using System.Collections.Generic;
using System.Net;
using YGOProSharp.Network;

namespace YGOProSharp
{
    public class CoreServer
    {
        public const int DefaultPort = 7911;

        public bool IsRunning { get; private set; }
        public bool IsListening { get; private set; }
        public AddonsManager? Addons { get; private set; }
        public Game? Game { get; private set; }

        private NetworkServer? _listener;
        private readonly List<YGOClient> _clients = new();

        private bool _closePending;

        public CoreServer()
        {
        }

        public void Start()
        {
            if (IsRunning)
                return;
            Addons = new AddonsManager();
            Game = new Game(this);
            Addons.Init(Game);
            try
            {
                _listener = new NetworkServer(IPAddress.Any, Config.GetInt("Port", DefaultPort));
                _listener.ClientConnected += Listener_ClientConnected;
                _listener.Start();
                IsRunning = true;
                IsListening = true;
                Game.Start();
            }
            catch (Exception)
            {
                //ignore
            }
        }

        public void StopListening()
        {
            if (!IsListening)
                return;
            IsListening = false;
            _listener?.Close();
        }

        public void Stop()
        {
            StopListening();
            foreach (YGOClient client in _clients)
                client.Close();
            Game?.Stop();
            IsRunning = false;
        }

        public void StopDelayed()
        {
            StopListening();
            _closePending = true;
        }

        public void AddClient(YGOClient client)
        {
            _clients.Add(client);
            if (Game is null)
                throw new InvalidOperationException("Server game has not been initialized.");

            Player player = new Player(Game, client);

            client.PacketReceivedRaw += packet => player.Parse(packet.Span);
            client.Disconnected += packet => player.OnDisconnected();
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
            AddClient(new YGOClient(client));
        }
    }
}
