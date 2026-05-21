using System.IO;
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
                _logger.LogDebug("Sending CTOS {CtosMessage}.", message);
                Send(writer);
            }
        }

        public void Send(CtosMessage message, int value)
        {
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                writer.Write((byte)message);
                writer.Write(value);
                _logger.LogDebug("Sending CTOS {CtosMessage} with value {Value}.", message, value);
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
                _logger.LogDebug("Sending STOC {StocMessage}/{GameMessage}. Length={PacketLength}.", message, (GameMessage)packet[1], packet.Length);
                return;
            }

            _logger.LogDebug("Sending STOC {StocMessage}. Length={PacketLength}.", message, packet.Length);
        }
    }
}
