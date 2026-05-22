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

        public static BinaryWriter CreateDeckCount(int player0Main, int player0Extra, int player0Side, int player1Main, int player1Extra, int player1Side)
        {
            BinaryWriter writer = Create(StocMessage.DeckCount);
            writer.Write((short)player0Main);
            writer.Write((short)player0Extra);
            writer.Write((short)player0Side);
            writer.Write((short)player1Main);
            writer.Write((short)player1Extra);
            writer.Write((short)player1Side);
            return writer;
        }

        public static BinaryWriter CreateFieldFinish()
        {
            return Create(StocMessage.FieldFinish);
        }
    }
}
