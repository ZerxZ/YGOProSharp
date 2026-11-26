using System.Security.Cryptography;
using System.Text;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Core;

public sealed record DeckCardFeatureItem(
    int Id,
    string Name,
    int Copies,
    string Section,
    int Type,
    int Level,
    int Attack,
    int Defense,
    int Race,
    int Attribute,
    long Setcode,
    bool IsDiy,
    string Description);

public sealed record DeckSetcodeCount(
    string SetcodeHex,
    int TotalCopies,
    IReadOnlyList<string> SampleCardNames);

public sealed record DeckEffectKeywordStats(
    int SearchOrAddHand,
    int SpecialSummon,
    int NegateOrDisable,
    int Destroy,
    int Banish,
    int DrawCards,
    int GraveInteraction,
    int FloodgateOrLock);

public sealed record DeckStaticProfile(
    string DeckName,
    string DeckHash,
    int MainCount,
    int ExtraCount,
    int SideCount,
    int DistinctCardCount,
    int DiyCardCopies,
    int LowLevelMonsterCopies,
    int HighLevelMonsterCopies,
    int TunerCopies,
    int PendulumCopies,
    int RitualMonsterCopies,
    int NormalMonsterCopies,
    int EffectMonsterCopies,
    int NormalSpellCopies,
    int QuickPlaySpellCopies,
    int ContinuousSpellCopies,
    int FieldSpellCopies,
    int EquipSpellCopies,
    int RitualSpellCopies,
    int NormalTrapCopies,
    int ContinuousTrapCopies,
    int CounterTrapCopies,
    int FusionCopies,
    int SynchroCopies,
    int XyzCopies,
    int LinkCopies,
    IReadOnlyList<DeckSetcodeCount> TopSetcodes,
    DeckEffectKeywordStats KeywordStats,
    IReadOnlyList<DeckCardFeatureItem> MainCards,
    IReadOnlyList<DeckCardFeatureItem> ExtraCards,
    IReadOnlyList<DeckCardFeatureItem> SideCards);

/// <summary>
/// 纯规则静态卡组构筑特征剖析器（零外部模型依赖）：
/// 计算顺序无关的规范化卡组哈希（<c>DeckHash</c>），并输出怪魔陷配比、星级曲线、本家字段聚簇与效果关键词客观基线，
/// 供外部 MCP AI 进行深度六维量化评分与展开套路提炼。
/// </summary>
public static class DeckStaticProfileBuilder
{
    public static string ComputeCanonicalDeckHash(YdkDeckData deck)
    {
        ArgumentNullException.ThrowIfNull(deck);

        StringBuilder sb = new(512);
        sb.Append("M:");
        foreach (int id in deck.Main.OrderBy(static x => x))
            sb.Append(id).Append(',');
        sb.Append("|E:");
        foreach (int id in deck.Extra.OrderBy(static x => x))
            sb.Append(id).Append(',');
        sb.Append("|S:");
        foreach (int id in deck.Side.OrderBy(static x => x))
            sb.Append(id).Append(',');

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    public static DeckStaticProfile Analyze(
        YdkDeckData deck,
        INamedCardRepository namedRepo,
        ICardRepository? fallbackCardRepo = null)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(namedRepo);

        string deckHash = ComputeCanonicalDeckHash(deck);
        List<DeckCardFeatureItem> mainItems = BuildSectionItems(deck.Main, "main", namedRepo, fallbackCardRepo);
        List<DeckCardFeatureItem> extraItems = BuildSectionItems(deck.Extra, "extra", namedRepo, fallbackCardRepo);
        List<DeckCardFeatureItem> sideItems = BuildSectionItems(deck.Side, "side", namedRepo, fallbackCardRepo);

        int diyCopies = 0;
        int lowMonster = 0, highMonster = 0, tuner = 0, pendulum = 0, ritualMonster = 0, normalMonster = 0, effectMonster = 0;
        int normalSpell = 0, quickSpell = 0, contSpell = 0, fieldSpell = 0, equipSpell = 0, ritualSpell = 0;
        int normalTrap = 0, contTrap = 0, counterTrap = 0;
        int fusion = 0, synchro = 0, xyz = 0, link = 0;

        int kwSearch = 0, kwSpSummon = 0, kwNegate = 0, kwDestroy = 0, kwBanish = 0, kwDraw = 0, kwGrave = 0, kwLock = 0;
        Dictionary<int, (int Copies, HashSet<string> Names)> setcodeBuckets = new();

        foreach (DeckCardFeatureItem item in mainItems.Concat(extraItems))
        {
            int c = item.Copies;
            CardType t = (CardType)item.Type;
            if (item.IsDiy)
                diyCopies += c;

            AccumulateSetcodes(item.Setcode, c, item.Name, setcodeBuckets);

            string desc = item.Description ?? string.Empty;
            if (ContainsAny(desc, "加入手卡", "从卡组把", "检索", "add 1", "from your Deck to your hand"))
                kwSearch += c;
            if (ContainsAny(desc, "特殊召唤", "Special Summon"))
                kwSpSummon += c;
            if (ContainsAny(desc, "无效", "negate", "不能发动"))
                kwNegate += c;
            if (ContainsAny(desc, "破坏", "destroy"))
                kwDestroy += c;
            if (ContainsAny(desc, "除外", "banish"))
                kwBanish += c;
            if (ContainsAny(desc, "抽1张", "抽2张", "抽卡", "Draw "))
                kwDraw += c;
            if (ContainsAny(desc, "墓地", "GY", "Graveyard"))
                kwGrave += c;
            if (ContainsAny(desc, "双方不能", "对方不能", "不能特殊召唤", "场上怪兽的效果无效化", "送去墓地的卡不去墓地", "neither player can"))
                kwLock += c;

            if ((t & CardType.Monster) != 0)
            {
                bool isExtraType = (t & (CardType.Fusion | CardType.Synchro | CardType.Xyz | CardType.Link)) != 0;
                if (isExtraType)
                {
                    if ((t & CardType.Link) != 0) link += c;
                    else if ((t & CardType.Xyz) != 0) xyz += c;
                    else if ((t & CardType.Synchro) != 0) synchro += c;
                    else if ((t & CardType.Fusion) != 0) fusion += c;
                }
                else
                {
                    if (item.Level <= 4) lowMonster += c;
                    else highMonster += c;

                    if ((t & CardType.Ritual) != 0) ritualMonster += c;
                    if ((t & CardType.Normal) != 0) normalMonster += c;
                    if ((t & CardType.Effect) != 0) effectMonster += c;
                }

                if ((t & CardType.Tuner) != 0) tuner += c;
                if ((t & CardType.Pendulum) != 0) pendulum += c;
            }
            else if ((t & CardType.Spell) != 0)
            {
                if ((t & CardType.QuickPlay) != 0) quickSpell += c;
                else if ((t & CardType.Continuous) != 0) contSpell += c;
                else if ((t & CardType.Field) != 0) fieldSpell += c;
                else if ((t & CardType.Equip) != 0) equipSpell += c;
                else if ((t & CardType.Ritual) != 0) ritualSpell += c;
                else normalSpell += c;
            }
            else if ((t & CardType.Trap) != 0)
            {
                if ((t & CardType.Counter) != 0) counterTrap += c;
                else if ((t & CardType.Continuous) != 0) contTrap += c;
                else normalTrap += c;
            }
        }

        DeckSetcodeCount[] topSetcodes = setcodeBuckets
            .OrderByDescending(static kv => kv.Value.Copies)
            .ThenBy(static kv => kv.Key)
            .Take(8)
            .Select(static kv => new DeckSetcodeCount($"0x{kv.Key:x}", kv.Value.Copies, kv.Value.Names.Take(5).ToArray()))
            .ToArray();

        int distinctCards = mainItems.Concat(extraItems).Concat(sideItems).Select(static x => x.Id).Distinct().Count();

        return new DeckStaticProfile(
            DeckName: string.IsNullOrWhiteSpace(deck.Name) ? "UnnamedDeck" : deck.Name,
            DeckHash: deckHash,
            MainCount: deck.Main.Count,
            ExtraCount: deck.Extra.Count,
            SideCount: deck.Side.Count,
            DistinctCardCount: distinctCards,
            DiyCardCopies: diyCopies,
            LowLevelMonsterCopies: lowMonster,
            HighLevelMonsterCopies: highMonster,
            TunerCopies: tuner,
            PendulumCopies: pendulum,
            RitualMonsterCopies: ritualMonster,
            NormalMonsterCopies: normalMonster,
            EffectMonsterCopies: effectMonster,
            NormalSpellCopies: normalSpell,
            QuickPlaySpellCopies: quickSpell,
            ContinuousSpellCopies: contSpell,
            FieldSpellCopies: fieldSpell,
            EquipSpellCopies: equipSpell,
            RitualSpellCopies: ritualSpell,
            NormalTrapCopies: normalTrap,
            ContinuousTrapCopies: contTrap,
            CounterTrapCopies: counterTrap,
            FusionCopies: fusion,
            SynchroCopies: synchro,
            XyzCopies: xyz,
            LinkCopies: link,
            TopSetcodes: topSetcodes,
            KeywordStats: new DeckEffectKeywordStats(
                SearchOrAddHand: kwSearch,
                SpecialSummon: kwSpSummon,
                NegateOrDisable: kwNegate,
                Destroy: kwDestroy,
                Banish: kwBanish,
                DrawCards: kwDraw,
                GraveInteraction: kwGrave,
                FloodgateOrLock: kwLock),
            MainCards: mainItems,
            ExtraCards: extraItems,
            SideCards: sideItems);
    }

    private static List<DeckCardFeatureItem> BuildSectionItems(
        IReadOnlyList<int> ids,
        string section,
        INamedCardRepository namedRepo,
        ICardRepository? fallbackRepo)
    {
        List<DeckCardFeatureItem> list = new();
        foreach (IGrouping<int, int> group in ids.GroupBy(static id => id))
        {
            int code = group.Key;
            int copies = group.Count();
            if (namedRepo.TryGetCard(code, out NamedCard? nc) && nc is not null)
            {
                list.Add(new DeckCardFeatureItem(
                    Id: code,
                    Name: string.IsNullOrWhiteSpace(nc.Name) ? $"#{code}" : nc.Name,
                    Copies: copies,
                    Section: section,
                    Type: nc.Type,
                    Level: nc.Level & 0xff,
                    Attack: nc.Attack,
                    Defense: nc.Defense,
                    Race: nc.Race,
                    Attribute: nc.Attribute,
                    Setcode: nc.Setcode,
                    IsDiy: (nc.Ot & 0x4) != 0,
                    Description: nc.Description ?? string.Empty));
            }
            else if (fallbackRepo is not null && fallbackRepo.TryGetCard(code, out Card raw))
            {
                list.Add(new DeckCardFeatureItem(
                    Id: code,
                    Name: $"#{code}",
                    Copies: copies,
                    Section: section,
                    Type: raw.Type,
                    Level: raw.Level & 0xff,
                    Attack: raw.Attack,
                    Defense: raw.Defense,
                    Race: raw.Race,
                    Attribute: raw.Attribute,
                    Setcode: raw.Setcode,
                    IsDiy: (raw.Ot & 0x4) != 0,
                    Description: string.Empty));
            }
            else
            {
                list.Add(new DeckCardFeatureItem(
                    Id: code,
                    Name: $"#{code}",
                    Copies: copies,
                    Section: section,
                    Type: 0,
                    Level: 0,
                    Attack: 0,
                    Defense: 0,
                    Race: 0,
                    Attribute: 0,
                    Setcode: 0,
                    IsDiy: false,
                    Description: string.Empty));
            }
        }
        return list;
    }

    private static void AccumulateSetcodes(
        long rawSetcode,
        int copies,
        string cardName,
        Dictionary<int, (int Copies, HashSet<string> Names)> buckets)
    {
        long v = rawSetcode;
        HashSet<int> seenOnCard = new();
        for (int slot = 0; slot < 4 && v != 0; slot++)
        {
            int code = (int)(v & 0xffff);
            v >>= 16;
            if (code <= 0 || !seenOnCard.Add(code))
                continue;

            if (!buckets.TryGetValue(code, out var entry))
                entry = (0, new HashSet<string>(StringComparer.Ordinal));
            entry.Names.Add(cardName);
            buckets[code] = (entry.Copies + copies, entry.Names);
        }
    }

    private static bool ContainsAny(string source, params string[] patterns)
    {
        if (string.IsNullOrEmpty(source))
            return false;
        for (int i = 0; i < patterns.Length; i++)
        {
            if (source.Contains(patterns[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
