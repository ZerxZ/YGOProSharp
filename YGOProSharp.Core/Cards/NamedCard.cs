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
        string description,
        IReadOnlyList<string>? effectStrings = null)
        : base(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker)
    {
        Name = name;
        Description = description;
        EffectStrings = effectStrings ?? [];
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>
    /// 对应 <c>texts.str1 .. str16</c> 的卡片脚本子效果提示文本（供 <c>aux.Stringid(code, offset)</c> 解码可读描述）。
    /// </summary>
    public IReadOnlyList<string> EffectStrings { get; }

    public string? GetEffectString(int offset)
    {
        if (offset < 0 || offset >= EffectStrings.Count)
            return null;
        string s = EffectStrings[offset];
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

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

    /// <summary>
    /// 判断指定卡号与别名是否属于异画关系（规则上视作同名卡的别名差值通常 >= 20，异画卡差值 &lt; 20）。
    /// </summary>
    public static bool IsAltartAlias(int cardId, int alias)
    {
        // YGOPro 将 5405695（仪式怪兽的通常怪兽版）视作规则同名别名而非异画。
        if (cardId == 5405695)
            return false;

        return cardId > 0 && alias > 0 && Math.Abs(cardId - alias) < 20;
    }
}
