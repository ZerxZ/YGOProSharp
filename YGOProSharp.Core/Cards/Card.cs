using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Core.Cards;

public class Card
{
    public Card(
        int id,
        int ot,
        int alias,
        long setcode,
        int type,
        int level,
        int lScale,
        int rScale,
        int race,
        int attribute,
        int attack,
        int defense,
        int linkMarker = 0)
    {
        Id = id;
        Ot = ot;
        Alias = alias;
        Setcode = setcode;
        Type = type;
        Level = level;
        LScale = lScale;
        RScale = rScale;
        Race = race;
        Attribute = attribute;
        Attack = attack;
        Defense = defense;
        LinkMarker = linkMarker;
    }

    public int Id { get; }

    public int Ot { get; }

    public int Alias { get; }

    public long Setcode { get; }

    public int Type { get; }

    public int Level { get; }

    public int LScale { get; }

    public int RScale { get; }

    public int LinkMarker { get; }

    public int Attribute { get; }

    public int Race { get; }

    public int Attack { get; }

    public int Defense { get; }

    public bool HasType(CardType type)
    {
        return (Type & (int)type) != 0;
    }

    public bool IsExtraCard()
    {
        return HasType(CardType.Fusion)
            || HasType(CardType.Synchro)
            || HasType(CardType.Xyz)
            || HasType(CardType.Link);
    }
}
