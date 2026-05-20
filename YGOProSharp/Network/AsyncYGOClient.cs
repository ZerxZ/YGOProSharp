using System.IO;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Logging;
using YGOProSharp.Network.Enums;

namespace YGOProSharp.Network
{
    public class AsyncYGOClient : AsyncBinaryClient
    {
        private readonly ILogger<AsyncYGOClient> _logger = AppLog.CreateLogger<AsyncYGOClient>();

        public AsyncYGOClient()
            : base(new NetworkClient())
        {
        }

        public AsyncYGOClient(NetworkClient client)
            : base(client)
        {
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
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                writer.Write((byte)message);
                _logger.LogDebug("Sending async CTOS {CtosMessage}.", message);
                Send(writer);
            }
        }

        public void Send(CtosMessage message, int value)
        {
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                writer.Write((byte)message);
                writer.Write(value);
                _logger.LogDebug("Sending async CTOS {CtosMessage} with value {Value}.", message, value);
                Send(writer);
            }
        }

        private void LogOutgoingPacket(ReadOnlySpan<byte> packet)
        {
            if (packet.IsEmpty)
                return;

            StocMessage message = (StocMessage)packet[0];
            if (message == StocMessage.GameMsg && packet.Length > 1)
            {
                _logger.LogDebug("Sending async STOC {StocMessage}/{GameMessage}. Length={PacketLength}.", message, (GameMessage)packet[1], packet.Length);
                return;
            }

            _logger.LogDebug("Sending async STOC {StocMessage}. Length={PacketLength}.", message, packet.Length);
        }
    }
}
