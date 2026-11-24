using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;
using YGOProSharp.Core.Cards;

namespace WindBot.Game
{
    public class GameClient : IDisposable
    {
        private static long _nextInstanceId;

        public YGOClient Connection { get; private set; }
        public long InstanceId { get; private set; }
        public string ExecutorName { get; private set; }
        public string CurrentSTOCMessage { get; private set; }
        public bool IsConnected => Connection != null && Connection.IsConnected;
        public event Action<Exception> Disconnected;
        public string Username;
        public string Deck;
        public string DeckFile;
        public string Dialog;
        public int Hand;
        public bool Debug;
        public bool _chat;
        private string _serverHost;
        private int _serverPort;
        private short _proVersion;
        private readonly INamedCardRepository _cardRepository;
        private INetworkTransport _customTransport;
        private readonly bool _useWebSocket;
        private readonly bool _webSocketUseWss;
        private readonly string _webSocketPath;
        private readonly WebSocketFramingMode _webSocketFramingMode;
        private string _externalAddressHost;

        private string _roomInfo;

        private GameBehavior _behavior;
        private bool _pendingInitialConnect;
        private bool _disposed;

        public GameClient(WindBotInfo Info)
            : this(Info, CardDatabase.Repository, null)
        {
        }

        public GameClient(WindBotInfo Info, INetworkTransport customTransport)
            : this(Info, CardDatabase.Repository, customTransport)
        {
        }

        public GameClient(WindBotInfo Info, INamedCardRepository cardRepository)
            : this(Info, cardRepository, null)
        {
        }

        public GameClient(WindBotInfo Info, INamedCardRepository cardRepository, INetworkTransport customTransport)
        {
            ArgumentNullException.ThrowIfNull(Info);
            InstanceId = Interlocked.Increment(ref _nextInstanceId);
            Username = Info.Name;
            Deck = Info.Deck;
            DeckFile = Info.DeckFile;
            Dialog = Info.Dialog;
            Hand = Info.Hand;
            Debug = Info.Debug;
            _chat = Info.Chat;
            _serverHost = Info.Host;
            _serverPort = Info.Port;
            _roomInfo = Info.HostInfo;
            _proVersion = (short)Info.Version;
            _cardRepository = cardRepository ?? EmptyNamedCardRepository.Instance;
            _customTransport = customTransport;
            _useWebSocket = Info.UseWebSocket;
            _webSocketUseWss = Info.WebSocketUseWss;
            _webSocketPath = Info.WebSocketPath;
            _webSocketFramingMode = Info.WebSocketFramingMode;
            _externalAddressHost = Info.Host;
        }

        internal void SetDeckContext(string executorName)
        {
            ExecutorName = executorName;
        }

        internal void SetCurrentSTOCMessage(string message)
        {
            CurrentSTOCMessage = message;
        }

        public string GetLogContext()
        {
            return "Instance=" + InstanceId
                + ", Bot=" + (Username ?? "<null>")
                + ", Executor=" + (ExecutorName ?? "<not initialized>")
                + ", STOCMessage=" + (CurrentSTOCMessage ?? "<none>");
        }

        /// <summary>
        /// 使用外部提供的 <see cref="INetworkTransport"/>（例如 <see cref="InMemoryNetworkTransport"/>）启动客户端，不走 TCP/WebSocket。
        /// </summary>
        public void Start(INetworkTransport transport)
        {
            _customTransport = transport ?? throw new ArgumentNullException(nameof(transport));
            Start();
        }

        public void Start()
        {
            CleanupRuntime();
            _disposed = false;

            if (_customTransport != null)
            {
                _externalAddressHost = string.IsNullOrWhiteSpace(_serverHost) ? "127.0.0.1" : _serverHost;
                Connection = new YGOClient(_customTransport);
                InitializeBehaviorOrCleanup();

                Connection.Connected += OnConnected;
                Connection.PacketReceived += OnPacketReceived;
                Connection.Disconnected += OnDisconnected;

                if (_customTransport is InMemoryNetworkTransport inMemory && !inMemory.IsConnected)
                    inMemory.Connect();
                else if (_customTransport.IsConnected)
                    _pendingInitialConnect = true;

                return;
            }

            if (TryBuildWebSocketUri(out Uri wsUri))
            {
                _externalAddressHost = wsUri.Host;
                Connection = YGOClient.CreateWebSocket(_webSocketFramingMode);
                InitializeBehaviorOrCleanup();

                Connection.Connected += OnConnected;
                Connection.PacketReceived += OnPacketReceived;
                Connection.Disconnected += OnDisconnected;
                Connection.Connect(wsUri);
                return;
            }

            _externalAddressHost = _serverHost;
            Connection = new YGOClient();
            InitializeBehaviorOrCleanup();

            Connection.Connected += OnConnected;
            Connection.PacketReceived += OnPacketReceived;
            Connection.Disconnected += OnDisconnected;

            IPAddress target_address;
            try
            {
                target_address = IPAddress.Parse(_serverHost);
            }
            catch (System.Exception)
            {
                IPHostEntry _hostEntry = Dns.GetHostEntry(_serverHost);
                target_address = _hostEntry.AddressList.FirstOrDefault(findIPv4 => findIPv4.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? _hostEntry.AddressList.FirstOrDefault();
            }

            if (target_address == null)
            {
                CleanupRuntime();
                throw new InvalidOperationException($"Could not resolve WindBot target host '{_serverHost}'.");
            }

            Connection.Connect(target_address, _serverPort);
        }

        private void InitializeBehaviorOrCleanup()
        {
            try
            {
                _behavior = new GameBehavior(this, _cardRepository);
            }
            catch
            {
                CleanupRuntime();
                throw;
            }
        }

        private bool TryBuildWebSocketUri(out Uri wsUri)
        {
            wsUri = null;
            if (string.IsNullOrWhiteSpace(_serverHost))
                return false;

            string trimmedHost = _serverHost.Trim();
            if (trimmedHost.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                trimmedHost.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(trimmedHost, UriKind.Absolute, out wsUri))
                    throw new InvalidOperationException($"Invalid WindBot WebSocket URI '{_serverHost}'.");

                return true;
            }

            if (!_useWebSocket)
                return false;

            UriBuilder builder = new()
            {
                Scheme = _webSocketUseWss ? Uri.UriSchemeWss : Uri.UriSchemeWs,
                Host = trimmedHost,
                Port = _serverPort > 0 ? _serverPort : (_webSocketUseWss ? 443 : 80),
                Path = string.IsNullOrWhiteSpace(_webSocketPath) ? "/" : _webSocketPath.Trim()
            };
            wsUri = builder.Uri;
            return true;
        }

        private void OnConnected()
        {
            using (BinaryWriter packet = GamePacketFactory.Create(CtosMessage.ExternalAddress))
            {
                packet.Write((UInt32)0); // real_ip, is always 0 in normal client
                packet.WriteUnicodeAutoLength(_externalAddressHost ?? string.Empty, 255);
                Connection.Send(packet);
            }

            using (BinaryWriter packet = GamePacketFactory.Create(CtosMessage.PlayerInfo))
            {
                packet.WriteUnicode(Username ?? "WindBot", 20);
                Connection.Send(packet);
            }

            byte[] junk = { 0xCC, 0xCC, 0x00, 0x00, 0x00, 0x00 };
            using (BinaryWriter packet = GamePacketFactory.Create(CtosMessage.JoinGame))
            {
                packet.Write(_proVersion);
                packet.Write(junk);
                packet.WriteUnicode(_roomInfo ?? string.Empty, 20);
                Connection.Send(packet);
            }
        }

        public void Tick()
        {
            if (Connection == null)
                return;

            if (_pendingInitialConnect)
            {
                _pendingInitialConnect = false;
                if (Connection.IsConnected)
                    OnConnected();
            }

            Connection.Update();
        }

        public void Chat(string message)
        {
            if (Connection == null)
                return;

            using (BinaryWriter chat = GamePacketFactory.Create(CtosMessage.Chat))
            {
                chat.WriteUnicodeAutoLength(message ?? string.Empty, 255);
                Connection.Send(chat);
            }
        }

        public void Surrender()
        {
            Connection?.Send(CtosMessage.Surrender);
        }

        private void OnPacketReceived(BinaryReader reader)
        {
            _behavior.OnPacket(reader);
        }

        private void OnDisconnected(Exception error)
        {
            Disconnected?.Invoke(error);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CleanupRuntime();
        }

        private void CleanupRuntime()
        {
            _pendingInitialConnect = false;

            if (Connection != null)
            {
                Connection.Connected -= OnConnected;
                Connection.PacketReceived -= OnPacketReceived;
                Connection.Disconnected -= OnDisconnected;
                Connection.Close();
                Connection = null;
            }

            _behavior?.Dispose();
            _behavior = null;
        }
    }
}
