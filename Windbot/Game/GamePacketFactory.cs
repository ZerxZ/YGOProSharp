using System.IO;
using YGOProSharp.Protocol.Enums;

namespace WindBot.Game
{
    public class GamePacketFactory
    {
        public static BinaryWriter Create(CtosMessage message)
        {
            BinaryWriter writer = new BinaryWriter(new MemoryStream());
            writer.Write((byte)message);
            return writer;
        }
    }
}

