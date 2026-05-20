using System.Runtime.InteropServices;

namespace YGOProSharp.Abstractions.Ocg;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct OcgCardData
{
    public uint Code;
    public uint Alias;
    public fixed ushort SetCode[OcgCoreConstants.SetCodeCount];
    public uint Type;
    public uint Level;
    public uint Attribute;
    public uint Race;
    public int Attack;
    public int Defense;
    public uint LScale;
    public uint RScale;
    public uint LinkMarker;
    public uint RuleCode;

    public static OcgCardData Create(
        uint code,
        uint alias,
        ulong setCode,
        uint type,
        uint level,
        uint attribute,
        uint race,
        int attack,
        int defense,
        uint lScale,
        uint rScale,
        uint linkMarker,
        uint ruleCode = 0)
    {
        OcgCardData data = new()
        {
            Code = code,
            Alias = alias,
            Type = type,
            Level = level,
            Attribute = attribute,
            Race = race,
            Attack = attack,
            Defense = defense,
            LScale = lScale,
            RScale = rScale,
            LinkMarker = linkMarker,
            RuleCode = ruleCode
        };

        data.WriteSetCode(setCode);
        return data;
    }

    public void WriteSetCode(ulong value)
    {
        fixed (ushort* target = SetCode)
        {
            int length = 0;
            while (value != 0 && length < OcgCoreConstants.SetCodeCount)
            {
                ushort item = (ushort)(value & 0xffff);
                if (item != 0)
                    target[length++] = item;

                value >>= 16;
            }

            for (int i = length; i < OcgCoreConstants.SetCodeCount; i++)
                target[i] = 0;
        }
    }
}
