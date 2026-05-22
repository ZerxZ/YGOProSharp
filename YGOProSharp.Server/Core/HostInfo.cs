using System.IO;
using YGOProSharp.Protocol;

namespace YGOProSharp.Server
{
    public readonly record struct HostInfo(
        uint LfList,
        int Region,
        int MasterRule,
        int Mode,
        bool EnablePriority,
        bool NoCheckDeck,
        bool NoShuffleDeck,
        int StartLp,
        int StartHand,
        int DrawCount,
        int TimeLimit)
    {
        public static HostInfo ReadFrom(BinaryReader reader)
        {
            uint lfList = reader.ReadUInt32();
            int region = reader.ReadByte();
            int masterRule = reader.ReadByte();
            int mode = reader.ReadByte();
            bool enablePriority = reader.ReadByte() > 0;
            bool noCheckDeck = reader.ReadByte() > 0;
            bool noShuffleDeck = reader.ReadByte() > 0;
            SkipPadding(reader);
            int startLp = reader.ReadInt32();
            int startHand = reader.ReadByte();
            int drawCount = reader.ReadByte();
            int timeLimit = reader.ReadInt16();

            return new HostInfo(lfList, region, masterRule, mode, enablePriority, noCheckDeck, noShuffleDeck, startLp, startHand, drawCount, timeLimit);
        }

        public static HostInfo ReadFrom(ref PacketReader reader)
        {
            uint lfList = reader.ReadUInt32();
            int region = reader.ReadByte();
            int masterRule = reader.ReadByte();
            int mode = reader.ReadByte();
            bool enablePriority = reader.ReadByte() > 0;
            bool noCheckDeck = reader.ReadByte() > 0;
            bool noShuffleDeck = reader.ReadByte() > 0;
            SkipPadding(ref reader);
            int startLp = reader.ReadInt32();
            int startHand = reader.ReadByte();
            int drawCount = reader.ReadByte();
            int timeLimit = reader.ReadInt16();

            return new HostInfo(lfList, region, masterRule, mode, enablePriority, noCheckDeck, noShuffleDeck, startLp, startHand, drawCount, timeLimit);
        }

        private static void SkipPadding(BinaryReader reader)
        {
            for (int i = 0; i < 3; i++)
                reader.ReadByte();
        }

        private static void SkipPadding(ref PacketReader reader)
        {
            for (int i = 0; i < 3; i++)
                reader.ReadByte();
        }
    }
}
