using System.IO;
using YGOProSharp.Network.Enums;

namespace YGOProSharp.Network
{
    public class YGOClient : BinaryClient
    {
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
            if (stream.TryGetBuffer(out ArraySegment<byte> segment))
                Send(segment.AsSpan());
            else
                Send(stream.ToArray());
        }

        public void Send(CtosMessage message)
        {
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                writer.Write((byte)message);
                Send(writer);
            }
        }

        public void Send(CtosMessage message, int value)
        {
            using (BinaryWriter writer = new BinaryWriter(new MemoryStream()))
            {
                writer.Write((byte)message);
                writer.Write(value);
                Send(writer);
            }
        }
    }
}
