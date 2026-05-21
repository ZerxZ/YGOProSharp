using System.IO;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Server
{
    public static class GamePacketFactory
    {
        public static BinaryWriter Create(StocMessage message)
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            writer.Write((byte)message);
            return writer;
        }

        public static BinaryWriter Create(GameMessage message)
        {
            BinaryWriter writer = Create(StocMessage.GameMsg);
            writer.Write((byte)message);
            return writer;
        } 
    }
}
