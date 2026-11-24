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
        public string Locale;
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

        private readonly object _chatQueueLock = new();
        private readonly Queue<string> _pendingChats = new();
        private DateTime _nextChatAllowedAtUtc = DateTime.MinValue;
        private readonly int _chatIntervalMilliseconds;
        private readonly int _duelStartChatDelayMilliseconds;
        private readonly int _maxQueuedChatMessages;

        private GameBehavior _behavior;
        private bool _pendingInitialConnect;
        private bool _disposed;

        /// <summary>
        /// 当前客户端连续聊天最小间隔（毫秒，0 表示不节流立即发送）。
        /// </summary>
        public int ChatIntervalMilliseconds => _chatIntervalMilliseconds;

        /// <summary>
        /// 对局开始时延后首条聊天的时间（毫秒）。
        /// </summary>
        public int DuelStartChatDelayMilliseconds => _duelStartChatDelayMilliseconds;

        /// <summary>
        /// 待发送聊天队列上限。
        /// </summary>
        public int MaxQueuedChatMessages => _maxQueuedChatMessages;

        /// <summary>
        /// 当前尚未发出的排队聊天条数。
        /// </summary>
        public int PendingChatCount
        {
            get
            {
                lock (_chatQueueLock)
                    return _pendingChats.Count;
            }
        }

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
            Locale = string.IsNullOrWhiteSpace(Info.Locale) ? WindBotRuntime.Locale : Info.Locale.Trim();
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
            _chatIntervalMilliseconds = Math.Max(0, Info.ChatIntervalMilliseconds ?? WindBotRuntime.ChatIntervalMilliseconds);
            _duelStartChatDelayMilliseconds = Math.Max(0, Info.DuelStartChatDelayMilliseconds ?? WindBotRuntime.DuelStartChatDelayMilliseconds);
            _maxQueuedChatMessages = Math.Max(1, Info.MaxQueuedChatMessages ?? WindBotRuntime.MaxQueuedChatMessages);
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
            FlushPendingChats();
        }

        /// <summary>
        /// 发送一条聊天消息。启用节流（<see cref="ChatIntervalMilliseconds"/> &gt; 0）时，首条满足冷却立即发出，后续密集消息按间隔逐条平滑发出，避免一次性倾倒。
        /// </summary>
        public void Chat(string message)
        {
            if (Connection == null || string.IsNullOrEmpty(message))
                return;

            DateTime nowUtc = DateTime.UtcNow;
            bool sendNow = false;

            lock (_chatQueueLock)
            {
                if (_chatIntervalMilliseconds <= 0 && nowUtc >= _nextChatAllowedAtUtc && _pendingChats.Count == 0)
                {
                    sendNow = true;
                }
                else if (_pendingChats.Count == 0 && nowUtc >= _nextChatAllowedAtUtc)
                {
                    sendNow = true;
                    _nextChatAllowedAtUtc = nowUtc.AddMilliseconds(_chatIntervalMilliseconds);
                }
                else
                {
                    while (_pendingChats.Count >= _maxQueuedChatMessages)
                        _pendingChats.Dequeue();
                    _pendingChats.Enqueue(message);
                }
            }

            if (sendNow)
                SendChatPacket(message);
        }

        /// <summary>
        /// 绕过节流队列立即发送一条聊天消息（用于显式立即通知或单元测试）。
        /// </summary>
        public void ChatImmediate(string message)
        {
            if (Connection == null || string.IsNullOrEmpty(message))
                return;

            SendChatPacket(message);
        }

        /// <summary>
        /// 延后下一次聊天发送的允许时间（不阻塞线程），用于等待客户端完成切场加载。
        /// </summary>
        public void DelayChat(int delayMilliseconds)
        {
            if (delayMilliseconds <= 0)
                return;

            DateTime targetUtc = DateTime.UtcNow.AddMilliseconds(delayMilliseconds);
            lock (_chatQueueLock)
            {
                if (targetUtc > _nextChatAllowedAtUtc)
                    _nextChatAllowedAtUtc = targetUtc;
            }
        }

        /// <summary>
        /// 清空尚未发出的排队聊天消息（如对局结束或断线清理时调用）。
        /// </summary>
        public void ClearPendingChats()
        {
            lock (_chatQueueLock)
            {
                _pendingChats.Clear();
                _nextChatAllowedAtUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 尝试按节流间隔推送最多一条已到期的排队聊天消息（由 <see cref="Tick"/> 自动调用）。
        /// </summary>
        public bool FlushPendingChats()
        {
            if (Connection == null || !Connection.IsConnected)
                return false;

            string nextMessage = null;
            DateTime nowUtc = DateTime.UtcNow;

            lock (_chatQueueLock)
            {
                if (_pendingChats.Count == 0 || nowUtc < _nextChatAllowedAtUtc)
                    return false;

                nextMessage = _pendingChats.Dequeue();
                _nextChatAllowedAtUtc = _chatIntervalMilliseconds > 0
                    ? nowUtc.AddMilliseconds(_chatIntervalMilliseconds)
                    : nowUtc;
            }

            SendChatPacket(nextMessage);
            return true;
        }

        private void SendChatPacket(string message)
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
            ClearPendingChats();

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
