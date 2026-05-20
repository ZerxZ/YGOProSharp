using System.Data;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Cards
{
    public class Card
    {
        public int Id { get; private set; }
        public int Ot { get; private set; }
        public int Alias { get; private set; }
        public long Setcode { get; private set; }
        public int Type { get; private set; }

        public int Level { get; private set; }
        public int LScale { get; private set; }
        public int RScale { get; private set; }
        public int LinkMarker { get; private set; }

        public int Attribute { get; private set; }
        public int Race { get; private set; }
        public int Attack { get; private set; }
        public int Defense { get; private set; }

        internal OcgCardData Data { get; private set; }

        public static Card? Get(int id)
        {
            return CardsManager.GetCard(id);
        }

        public bool HasType(CardType type)
        {
            return ((Type & (int)type) != 0);
        }

        public bool IsExtraCard()
        {
            return (HasType(CardType.Fusion) || HasType(CardType.Synchro) || HasType(CardType.Xyz) || HasType(CardType.Link));
        }

        internal Card(IDataRecord reader)
        {
            Id = reader.GetInt32(0);
            Ot = reader.GetInt32(1);
            Alias = reader.GetInt32(2);
            Setcode = reader.GetInt64(3);
            Type = reader.GetInt32(4);

            int levelInfo = reader.GetInt32(5);
            Level = levelInfo & 0xff;
            LScale = (levelInfo >> 24) & 0xff;
            RScale = (levelInfo >> 16) & 0xff;

            Race = reader.GetInt32(6);
            Attribute = reader.GetInt32(7);
            Attack = reader.GetInt32(8);
            Defense = reader.GetInt32(9);

            if (HasType(CardType.Link))
            {
                LinkMarker = Defense;
                Defense = 0;
            }

            Data = OcgCardData.Create(
                (uint)Id,
                (uint)Alias,
                unchecked((ulong)Setcode),
                (uint)Type,
                (uint)Level,
                (uint)Attribute,
                (uint)Race,
                Attack,
                Defense,
                (uint)LScale,
                (uint)RScale,
                (uint)LinkMarker);
        }
    }
}
