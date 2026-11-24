using System.Runtime.InteropServices;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Core;

/// <summary>
/// 服务端随机卡组模式。
/// </summary>
public enum RandomDeckMode
{
    None = 0,
    PresetRandom = 1,
    MirrorRandom = 2,
    ChaosDraft = 3,
    PureChaos = 4,
    DiyRandom = 5,
    DiyChaos = 6
}

/// <summary>
/// 预设盲盒卡组池中的单套卡组条目。
/// </summary>
public sealed record PresetDeckEntry(
    string Id,
    string Name,
    string? SourcePath,
    YdkDeckData Data,
    bool HasDiyCards);

/// <summary>
/// 支持原子热刷新的服务端预设 <c>.ydk</c> 盲盒卡组池。
/// </summary>
public sealed class PresetDeckPool
{
    private readonly object _gate = new();
    private volatile PresetDeckEntry[] _entries = [];

    public IReadOnlyList<PresetDeckEntry> Entries => _entries;

    public int Count => _entries.Length;

    public void Clear()
    {
        lock (_gate)
            _entries = [];
    }

    public void AddOrUpdate(PresetDeckEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            List<PresetDeckEntry> next = new(_entries.Length + 1);
            bool replaced = false;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (string.Equals(_entries[i].Id, entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    next.Add(entry);
                    replaced = true;
                }
                else
                {
                    next.Add(_entries[i]);
                }
            }

            if (!replaced)
                next.Add(entry);

            _entries = next.ToArray();
        }
    }

    public bool Remove(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
        {
            PresetDeckEntry[] current = _entries;
            List<PresetDeckEntry> kept = new(current.Length);
            bool removed = false;
            for (int i = 0; i < current.Length; i++)
            {
                if (string.Equals(current[i].Id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    removed = true;
                else
                    kept.Add(current[i]);
            }

            if (removed)
                _entries = kept.ToArray();
            return removed;
        }
    }

    /// <summary>
    /// 扫描一个或多个目录下的 <c>*.ydk</c> 文件并原子替换内存卡组池快照（子目录名自动提取为分类标签）。
    /// </summary>
    public int ReloadFromDirectories(IEnumerable<string> directories, ICardRepository? cardRepository = null, IEnumerable<string>? additionalTags = null)
    {
        ArgumentNullException.ThrowIfNull(directories);

        string[] extraTags = additionalTags is null
            ? []
            : additionalTags.Where(static t => !string.IsNullOrWhiteSpace(t)).Select(static t => t.Trim()).ToArray();

        Dictionary<string, PresetDeckEntry> loaded = new(StringComparer.OrdinalIgnoreCase);
        foreach (string rawDir in directories)
        {
            if (string.IsNullOrWhiteSpace(rawDir))
                continue;

            string rootDir = Path.GetFullPath(rawDir);
            if (!Directory.Exists(rootDir))
                continue;

            foreach (string file in Directory.EnumerateFiles(rootDir, "*.ydk", SearchOption.AllDirectories)
                         .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                string relative = Path.GetRelativePath(rootDir, file);
                string? relDir = Path.GetDirectoryName(relative);
                List<string> tags = new(extraTags);
                if (!string.IsNullOrWhiteSpace(relDir) && relDir != ".")
                {
                    foreach (string segment in relDir.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
                        tags.Add(segment);
                }

                YdkDeckData data = YdkSerializer.LoadFile(file, tags);
                if (data.Main.Count == 0 && data.Extra.Count == 0)
                    continue;

                bool hasDiy = ContainsDiyCard(data, cardRepository);
                if (hasDiy && !data.Tags.Contains("diy", StringComparer.OrdinalIgnoreCase))
                    data = data with { Tags = [.. data.Tags, "diy"] };

                string id = relative.Replace('\\', '/');
                if (id.EndsWith(".ydk", StringComparison.OrdinalIgnoreCase))
                    id = id[..^4];

                loaded[id] = new PresetDeckEntry(id, data.Name, file, data, hasDiy);
            }
        }

        PresetDeckEntry[] snapshot = loaded.Values.ToArray();
        lock (_gate)
            _entries = snapshot;

        return snapshot.Length;
    }

    public IReadOnlyList<PresetDeckEntry> FilterCompatible(
        ICardRepository cardRepository,
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        DeckRules? rules = null,
        bool skipValidation = false,
        bool requireDiy = false)
    {
        ArgumentNullException.ThrowIfNull(cardRepository);
        DeckRules effectiveRules = rules ?? DeckRules.Default;
        PresetDeckEntry[] snapshot = _entries;
        List<PresetDeckEntry> compatible = new(snapshot.Length);

        for (int i = 0; i < snapshot.Length; i++)
        {
            PresetDeckEntry entry = snapshot[i];
            bool entryHasDiy = entry.HasDiyCards || ContainsDiyCard(entry.Data, cardRepository);
            if (requireDiy && !entryHasDiy && !entry.Data.Tags.Contains("diy", StringComparer.OrdinalIgnoreCase))
                continue;

            Deck deck = YdkSerializer.ToDeck(entry.Data, cardRepository);
            if (deck.Main.Count == 0)
                continue;

            if (skipValidation || deck.Check(banlist, ocg, tcg, allowCustom, effectiveRules) == 0)
                compatible.Add(entry);
        }

        return compatible;
    }

    private static bool ContainsDiyCard(YdkDeckData data, ICardRepository? repo)
    {
        if (repo is null)
            return false;

        foreach (int id in data.Main)
        {
            if (repo.TryGetCard(id, out Card c) && (c.Ot & 0x4) != 0)
                return true;
        }
        foreach (int id in data.Extra)
        {
            if (repo.TryGetCard(id, out Card c) && (c.Ot & 0x4) != 0)
                return true;
        }
        return false;
    }
}

/// <summary>
/// 混沌随机组卡配置项。
/// </summary>
public sealed record ChaosDraftOptions
{
    public int MainDeckSize { get; init; } = 40;
    public int ExtraDeckSize { get; init; } = 15;
    public bool BalancedRatio { get; init; } = true;
    public bool PreferDiyCards { get; init; }
}

/// <summary>
/// 单次随机生成的卡组结果（包含展示名称/标签与实际 <see cref="Deck"/> 实例）。
/// </summary>
public sealed record GeneratedRandomDeck(
    string Label,
    RandomDeckMode Mode,
    Deck Deck);

/// <summary>
/// 服务端随机卡组生成器（支持盲盒预组、1v1/2v2 对称镜像内战、平衡配比混沌组卡与 DIY 专属随机）。
/// </summary>
public sealed class RandomDeckGenerator
{
    private readonly ICardRepository _cardRepository;
    private readonly PresetDeckPool _presetPool;

    public RandomDeckGenerator(ICardRepository cardRepository, PresetDeckPool? presetPool = null)
    {
        ArgumentNullException.ThrowIfNull(cardRepository);
        _cardRepository = cardRepository;
        _presetPool = presetPool ?? new PresetDeckPool();
    }

    /// <summary>
    /// 为单个玩家生成一套符合指定规则与模式的随机卡组。
    /// </summary>
    public GeneratedRandomDeck GenerateSingle(
        RandomDeckMode mode,
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        DeckRules? rules = null,
        bool skipDeckValidation = false,
        int mainDeckSize = 40,
        Random? random = null)
    {
        Random rng = random ?? Random.Shared;
        DeckRules effectiveRules = rules ?? DeckRules.Default;
        bool effectiveAllowCustom = allowCustom || mode is RandomDeckMode.DiyRandom or RandomDeckMode.DiyChaos;

        if (mode is RandomDeckMode.PresetRandom or RandomDeckMode.MirrorRandom or RandomDeckMode.DiyRandom)
        {
            bool requireDiy = mode == RandomDeckMode.DiyRandom;
            IReadOnlyList<PresetDeckEntry> candidates = _presetPool.FilterCompatible(
                _cardRepository,
                banlist,
                ocg,
                tcg,
                effectiveAllowCustom,
                effectiveRules,
                skipValidation: skipDeckValidation,
                requireDiy: requireDiy);

            if (candidates.Count > 0)
            {
                PresetDeckEntry chosen = candidates[rng.Next(candidates.Count)];
                return new GeneratedRandomDeck(chosen.Name, mode, YdkSerializer.ToDeck(chosen.Data, _cardRepository));
            }

            // 若预设池尚无匹配 .ydk，优雅回退至混沌抽卡，保证对局不卡死。
            ChaosDraftOptions fallbackOpts = new()
            {
                MainDeckSize = Math.Clamp(mainDeckSize, Math.Max(1, effectiveRules.MainDeckMinSize), Math.Max(40, effectiveRules.MainDeckMaxSize)),
                ExtraDeckSize = Math.Clamp(15, 0, effectiveRules.ExtraDeckMaxSize),
                BalancedRatio = true,
                PreferDiyCards = requireDiy
            };
            Deck fallbackDeck = GenerateChaosDeck(banlist, ocg, tcg, effectiveAllowCustom, fallbackOpts, rng);
            return new GeneratedRandomDeck(requireDiy ? "DIY混沌补位" : "混沌随机补位", mode, fallbackDeck);
        }

        ChaosDraftOptions chaosOptions = new()
        {
            MainDeckSize = Math.Clamp(mainDeckSize, Math.Max(1, effectiveRules.MainDeckMinSize), Math.Max(40, effectiveRules.MainDeckMaxSize)),
            ExtraDeckSize = Math.Clamp(15, 0, effectiveRules.ExtraDeckMaxSize),
            BalancedRatio = mode != RandomDeckMode.PureChaos,
            PreferDiyCards = mode == RandomDeckMode.DiyChaos
        };
        Deck chaosDeck = GenerateChaosDeck(banlist, ocg, tcg, effectiveAllowCustom, chaosOptions, rng);
        string label = mode switch
        {
            RandomDeckMode.PureChaos => $"纯真随机({chaosDeck.Main.Count}+{chaosDeck.Extra.Count})",
            RandomDeckMode.DiyChaos => $"DIY混沌({chaosDeck.Main.Count}+{chaosDeck.Extra.Count})",
            _ => $"平衡混沌({chaosDeck.Main.Count}+{chaosDeck.Extra.Count})"
        };
        return new GeneratedRandomDeck(label, mode, chaosDeck);
    }

    /// <summary>
    /// 为整场对局（1v1 双席或 2v2 Tag 四席，按 <c>[Team0..., Team1...]</c> 顺序）分配随机卡组：
    /// <list type="bullet">
    /// <item><c>PresetRandom / DiyRandom</c>：尽量为各席位抽取不重复的预设卡组；</item>
    /// <item><c>MirrorRandom</c>：1v1 双方同构筑；2v2 下抽取 <c>(Deck X, Deck Y)</c> 令两队持有完全对称的双卡组组合；</item>
    /// <item><c>ChaosDraft / PureChaos / DiyChaos</c>：每席独立按配比实时生成。</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<GeneratedRandomDeck> AllocateDecksForDuel(
        int seatCount,
        RandomDeckMode mode,
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        DeckRules? rules = null,
        bool skipDeckValidation = false,
        int mainDeckSize = 40,
        Random? random = null)
    {
        if (seatCount <= 0)
            return [];

        Random rng = random ?? Random.Shared;
        DeckRules effectiveRules = rules ?? DeckRules.Default;
        bool effectiveAllowCustom = allowCustom || mode is RandomDeckMode.DiyRandom or RandomDeckMode.DiyChaos;

        if (mode == RandomDeckMode.MirrorRandom)
        {
            if (seatCount == 4)
            {
                // 2v2 Tag 队际镜像：抽取两套卡组 (Deck A, Deck B)，令 Team 0 (#0,#1) 与 Team 1 (#2,#3) 对称持有。
                IReadOnlyList<GeneratedRandomDeck> pair = AllocateDistinctOrChaos(
                    count: 2,
                    mode: RandomDeckMode.PresetRandom,
                    banlist,
                    ocg,
                    tcg,
                    effectiveAllowCustom,
                    effectiveRules,
                    skipDeckValidation,
                    mainDeckSize,
                    requireDiy: false,
                    rng);

                return
                [
                    CloneGenerated(pair[0], RandomDeckMode.MirrorRandom),
                    CloneGenerated(pair[1], RandomDeckMode.MirrorRandom),
                    CloneGenerated(pair[0], RandomDeckMode.MirrorRandom),
                    CloneGenerated(pair[1], RandomDeckMode.MirrorRandom)
                ];
            }

            GeneratedRandomDeck template = GenerateSingle(
                RandomDeckMode.MirrorRandom,
                banlist,
                ocg,
                tcg,
                effectiveAllowCustom,
                effectiveRules,
                skipDeckValidation,
                mainDeckSize,
                rng);

            GeneratedRandomDeck[] mirrorResults = new GeneratedRandomDeck[seatCount];
            for (int i = 0; i < seatCount; i++)
                mirrorResults[i] = CloneGenerated(template, RandomDeckMode.MirrorRandom);
            return mirrorResults;
        }

        if (mode is RandomDeckMode.PresetRandom or RandomDeckMode.DiyRandom)
        {
            return AllocateDistinctOrChaos(
                seatCount,
                mode,
                banlist,
                ocg,
                tcg,
                effectiveAllowCustom,
                effectiveRules,
                skipDeckValidation,
                mainDeckSize,
                requireDiy: mode == RandomDeckMode.DiyRandom,
                rng);
        }

        GeneratedRandomDeck[] results = new GeneratedRandomDeck[seatCount];
        for (int i = 0; i < seatCount; i++)
        {
            results[i] = GenerateSingle(
                mode,
                banlist,
                ocg,
                tcg,
                effectiveAllowCustom,
                effectiveRules,
                skipDeckValidation,
                mainDeckSize,
                rng);
        }

        return results;
    }

    public Deck GenerateChaosDeck(
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        ChaosDraftOptions? options = null,
        Random? random = null)
    {
        ChaosDraftOptions opts = options ?? new ChaosDraftOptions();
        Random rng = random ?? Random.Shared;

        List<Card> lowMonsters = new();
        List<Card> highMonsters = new();
        List<Card> spells = new();
        List<Card> traps = new();
        List<Card> fallbackMain = new();
        List<Card> fusions = new();
        List<Card> synchros = new();
        List<Card> xyzs = new();
        List<Card> links = new();

        List<Card> diyMain = new();
        List<Card> diyExtra = new();
        Dictionary<int, int> maxAllowedByCanonical = new();

        foreach (Card card in _cardRepository.GetAllCards())
        {
            if (!IsLegalCandidate(card, banlist, ocg, tcg, allowCustom, out int canonicalId, out int maxCopies))
                continue;

            maxAllowedByCanonical[canonicalId] = maxCopies;
            bool isDiy = (card.Ot & 0x4) != 0;

            if (card.IsExtraCard())
            {
                if (isDiy)
                    diyExtra.Add(card);

                if (card.HasType(CardType.Link))
                    links.Add(card);
                else if (card.HasType(CardType.Xyz))
                    xyzs.Add(card);
                else if (card.HasType(CardType.Synchro))
                    synchros.Add(card);
                else
                    fusions.Add(card);
            }
            else
            {
                if (isDiy)
                    diyMain.Add(card);

                if (card.HasType(CardType.Monster))
                {
                    if (card.HasType(CardType.Ritual))
                    {
                        fallbackMain.Add(card);
                    }
                    else if (card.Level <= 4)
                    {
                        lowMonsters.Add(card);
                    }
                    else if (!card.HasType(CardType.SpSummon))
                    {
                        highMonsters.Add(card);
                    }
                    else
                    {
                        fallbackMain.Add(card);
                    }
                }
                else if (card.HasType(CardType.Spell))
                {
                    if (card.HasType(CardType.Ritual))
                        fallbackMain.Add(card);
                    else
                        spells.Add(card);
                }
                else if (card.HasType(CardType.Trap))
                {
                    traps.Add(card);
                }
                else
                {
                    fallbackMain.Add(card);
                }
            }
        }

        Deck deck = new(_cardRepository);
        Dictionary<int, int> usedCounts = new();
        int targetMain = Math.Clamp(opts.MainDeckSize, 0, 60);
        int targetExtra = Math.Clamp(opts.ExtraDeckSize, 0, 15);

        if (opts.PreferDiyCards && diyMain.Count > 0)
        {
            PickFromPool(diyMain, targetMain, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
        }

        if (opts.BalancedRatio && deck.Main.Count < targetMain)
        {
            int remaining = targetMain - deck.Main.Count;
            int lowTarget = (int)Math.Round(remaining * 0.48);
            int highTarget = (int)Math.Round(remaining * 0.11);
            int spellTarget = (int)Math.Round(remaining * 0.23);
            int trapTarget = Math.Max(0, remaining - lowTarget - highTarget - spellTarget);

            PickFromPool(lowMonsters, lowTarget, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(highMonsters, highTarget, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(spells, spellTarget, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(traps, trapTarget, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
        }

        if (deck.Main.Count < targetMain)
        {
            List<Card> allMain = new(lowMonsters.Count + highMonsters.Count + spells.Count + traps.Count + fallbackMain.Count);
            allMain.AddRange(lowMonsters);
            allMain.AddRange(highMonsters);
            allMain.AddRange(spells);
            allMain.AddRange(traps);
            allMain.AddRange(fallbackMain);
            PickFromPool(allMain, targetMain - deck.Main.Count, deck, isExtra: false, usedCounts, maxAllowedByCanonical, rng);
        }

        if (opts.PreferDiyCards && diyExtra.Count > 0)
        {
            PickFromPool(diyExtra, targetExtra, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
        }

        if (opts.BalancedRatio && deck.Extra.Count < targetExtra)
        {
            bool hasTuner = deck.Main.Any(id => _cardRepository.TryGetCard(id, out Card c) && c.HasType(CardType.Tuner));
            int remExtra = targetExtra - deck.Extra.Count;
            int linkTarget = Math.Max(1, remExtra / 3);
            int xyzTarget = Math.Max(1, remExtra / 3);
            int synchroTarget = hasTuner ? Math.Max(1, remExtra / 4) : remExtra / 6;
            int fusionTarget = Math.Max(0, remExtra - linkTarget - xyzTarget - synchroTarget);

            PickFromPool(links, linkTarget, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(xyzs, xyzTarget, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(synchros, synchroTarget, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
            PickFromPool(fusions, fusionTarget, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
        }

        if (deck.Extra.Count < targetExtra)
        {
            List<Card> allExtra = new(links.Count + xyzs.Count + synchros.Count + fusions.Count);
            allExtra.AddRange(links);
            allExtra.AddRange(xyzs);
            allExtra.AddRange(synchros);
            allExtra.AddRange(fusions);
            PickFromPool(allExtra, targetExtra - deck.Extra.Count, deck, isExtra: true, usedCounts, maxAllowedByCanonical, rng);
        }

        return deck;
    }

    private IReadOnlyList<GeneratedRandomDeck> AllocateDistinctOrChaos(
        int count,
        RandomDeckMode mode,
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        DeckRules rules,
        bool skipValidation,
        int mainDeckSize,
        bool requireDiy,
        Random rng)
    {
        IReadOnlyList<PresetDeckEntry> candidates = _presetPool.FilterCompatible(
            _cardRepository,
            banlist,
            ocg,
            tcg,
            allowCustom,
            rules,
            skipValidation,
            requireDiy);

        GeneratedRandomDeck[] output = new GeneratedRandomDeck[count];
        if (candidates.Count > 0)
        {
            List<PresetDeckEntry> bag = new(candidates);
            for (int i = 0; i < count; i++)
            {
                if (bag.Count == 0)
                    bag.AddRange(candidates);

                int idx = rng.Next(bag.Count);
                PresetDeckEntry picked = bag[idx];
                bag.RemoveAt(idx);
                output[i] = new GeneratedRandomDeck(picked.Name, mode, YdkSerializer.ToDeck(picked.Data, _cardRepository));
            }
            return output;
        }

        RandomDeckMode fallbackChaosMode = requireDiy ? RandomDeckMode.DiyChaos : RandomDeckMode.ChaosDraft;
        for (int i = 0; i < count; i++)
        {
            output[i] = GenerateSingle(
                fallbackChaosMode,
                banlist,
                ocg,
                tcg,
                allowCustom,
                rules,
                skipValidation,
                mainDeckSize,
                rng);
        }

        return output;
    }

    private GeneratedRandomDeck CloneGenerated(GeneratedRandomDeck source, RandomDeckMode modeOverride)
    {
        Deck clone = new(_cardRepository);
        foreach (int id in source.Deck.Main)
            clone.AddMain(id);
        foreach (int id in source.Deck.Extra)
            clone.AddMain(id);
        foreach (int id in source.Deck.Side)
            clone.AddSide(id);

        return new GeneratedRandomDeck(source.Label, modeOverride, clone);
    }

    private static bool IsLegalCandidate(
        Card card,
        Banlist? banlist,
        bool ocg,
        bool tcg,
        bool allowCustom,
        out int canonicalId,
        out int maxCopies)
    {
        canonicalId = card.Alias != 0 ? card.Alias : card.Id;
        maxCopies = 0;

        if (card.Id <= 0 || card.HasType(CardType.Token))
            return false;

        if ((!ocg && card.Ot == 1) || (!tcg && card.Ot == 2))
            return false;

        if (!allowCustom && (card.Ot & 0x4) != 0)
            return false;

        int allowed = banlist is null ? 3 : Math.Clamp(banlist.GetQuantity(canonicalId), 0, 3);
        if (allowed <= 0)
            return false;

        maxCopies = allowed;
        return true;
    }

    private static void PickFromPool(
        List<Card> pool,
        int desiredCount,
        Deck targetDeck,
        bool isExtra,
        Dictionary<int, int> usedCounts,
        Dictionary<int, int> maxAllowedByCanonical,
        Random rng)
    {
        if (desiredCount <= 0 || pool.Count == 0)
            return;

        // 展开到允许张数上限的多重候选桶后做部分 Fisher-Yates 抽取，保证同名上限与禁限表 100% 不超标。
        List<Card> expanded = new(pool.Count * 2);
        for (int i = 0; i < pool.Count; i++)
        {
            Card card = pool[i];
            int canonical = card.Alias != 0 ? card.Alias : card.Id;
            int maxCopies = maxAllowedByCanonical.GetValueOrDefault(canonical, 3);
            int alreadyUsed = usedCounts.GetValueOrDefault(canonical, 0);
            int remaining = maxCopies - alreadyUsed;
            for (int copy = 0; copy < remaining; copy++)
                expanded.Add(card);
        }

        int added = 0;
        for (int i = expanded.Count - 1; i >= 0 && added < desiredCount; i--)
        {
            int swapIndex = rng.Next(i + 1);
            Card chosen = expanded[swapIndex];
            expanded[swapIndex] = expanded[i];

            int canonical = chosen.Alias != 0 ? chosen.Alias : chosen.Id;
            int maxCopies = maxAllowedByCanonical.GetValueOrDefault(canonical, 3);
            ref int currentUsed = ref CollectionsMarshal.GetValueRefOrAddDefault(usedCounts, canonical, out _);
            if (currentUsed >= maxCopies)
                continue;

            int beforeCount = isExtra ? targetDeck.Extra.Count : targetDeck.Main.Count;
            targetDeck.AddMain(chosen.Id);
            int afterCount = isExtra ? targetDeck.Extra.Count : targetDeck.Main.Count;
            if (afterCount > beforeCount)
            {
                currentUsed++;
                added++;
            }
        }
    }
}
