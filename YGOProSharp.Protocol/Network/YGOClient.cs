using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Protocol.Enums;

namespace YGOProSharp.Protocol
{
    public class YGOClient : BinaryClient
    {
        private readonly ILogger<YGOClient> _logger = AppLog.CreateLogger<YGOClient>();

        public YGOClient()
            : base(new NetworkClient())
        {
        }

        public YGOClient(NetworkClient client)
            : base(client)
        {
        }

        public YGOClient(INetworkTransport transport)
            : base(transport)
        {
        }

        /// <summary>
        /// 创建使用 WebSocket（<c>ws://</c> / <c>wss://</c>）传输的 <see cref="YGOClient"/>。
        /// </summary>
        public static YGOClient CreateWebSocket(
            WebSocketFramingMode framingMode = WebSocketFramingMode.LengthPrefixed,
            Action<ClientWebSocketOptions>? configureOptions = null)
        {
            return new YGOClient(new WebSocketNetworkClient(framingMode, configureOptions));
        }

        /// <summary>
        /// 创建一对通过进程内内存管道直连的 <see cref="YGOClient"/>（无需打开 TCP / WebSocket 端口）。
        /// </summary>
        public static (YGOClient Client, YGOClient Server) CreateInMemoryPair(
            IPAddress? clientAddress = null,
            IPAddress? serverAddress = null,
            bool connected = true)
        {
            (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
                InMemoryNetworkTransport.CreatePair(clientAddress, serverAddress, connected);
            return (new YGOClient(clientTransport), new YGOClient(serverTransport));
        }

        public void Send(BinaryWriter writer)
        {
            MemoryStream stream = (MemoryStream)writer.BaseStream;
            ReadOnlySpan<byte> packet = stream.TryGetBuffer(out ArraySegment<byte> segment)
                ? segment.AsSpan()
                : stream.ToArray();

            LogOutgoingPacket(packet);
            Send(packet);
        }

        public void Send(CtosMessage message)
        {
            // 栈上编码单字节 CTOS 包，消除 MemoryStream + BinaryWriter 堆分配。
            Span<byte> packet = [(byte)message];
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Sending CTOS {CtosMessage}.", message);
            Send(packet);
        }

        public void Send(CtosMessage message, int value)
        {
            Span<byte> packet = stackalloc byte[1 + sizeof(int)];
            packet[0] = (byte)message;
            BinaryPrimitives.WriteInt32LittleEndian(packet[1..], value);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Sending CTOS {CtosMessage} with value {Value}.", message, value);
            Send(packet);
        }

        private void LogOutgoingPacket(ReadOnlySpan<byte> packet)
        {
            if (packet.IsEmpty || !_logger.IsEnabled(LogLevel.Debug))
                return;

            StocMessage message = (StocMessage)packet[0];
            if (message == StocMessage.GameMsg && packet.Length > 1)
            {
                _logger.LogDebug("Sending STOC {StocMessage}/{GameMessage}. Length={PacketLength}.", message, (GameMessage)packet[1], packet.Length);
                return;
            }

            _logger.LogDebug("Sending STOC {StocMessage}. Length={PacketLength}.", message, packet.Length);
        }
    }
}
