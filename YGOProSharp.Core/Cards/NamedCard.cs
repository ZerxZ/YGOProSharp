namespace YGOProSharp.Core.Cards;

public class NamedCard : Card
{
    public NamedCard(
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
        int linkMarker,
        string name,
        string description)
        : base(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }

    public bool HasSetcode(int setcode)
    {
        long value = Setcode;
        int lowBits = setcode & 0xFFF;
        int highBits = setcode & 0xF000;

        while (value > 0)
        {
            long segment = value & 0xFFFF;
            value >>= 16;
            if ((segment & 0xFFF) == lowBits && (segment & 0xF000 & highBits) == highBits)
                return true;
        }

        return false;
    }
}
