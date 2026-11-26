namespace YGOProSharp.Tests;

public sealed class CoreAndNativeTests
{
    [Fact]
    public void AppLogParsesLogLevels()
    {
        AssertEqual((int)LogLevel.Information, (int)AppLog.ParseLevel(null));
        AssertEqual((int)LogLevel.Debug, (int)AppLog.ParseLevel("Debug"));
        AssertEqual((int)LogLevel.Trace, (int)AppLog.ParseLevel("trace"));
        AssertEqual((int)LogLevel.Warning, (int)AppLog.ParseLevel("nope", LogLevel.Warning));
    }

    [Fact]
    public void CoreMessageReadsNativePayloadsFromSpans()
    {
        byte[] raw =
        [
            0x34, 0x12,
            0x78, 0x56, 0x34, 0x12,
            0xAA, 0xBB, 0xCC
        ];

        CoreMessage message = new(GameMessage.Hint, raw);

        AssertEqual(0x1234, message.Reader.ReadInt16());
        AssertEqual(0x12345678, message.Reader.ReadInt32());
        AssertSequenceEqual(new byte[] { 0xAA, 0xBB }, message.Reader.ReadBytes(2));
        AssertSequenceEqual(raw.AsSpan(0, 8), message.CreateBufferSpan());
    }

    [Fact]
    public void NativeDuelRejectsTooSmallQueryDestinationBuffers()
    {
        byte[] source = [1, 2, 3, 4];
        byte[] destination = new byte[3];

        AssertThrows<ArgumentException>(() => NativeDuelSession.CopyQueryResult(source, source.Length, destination, "test_query"));
    }

    [Fact]
    public void NativeDuelPreservesOversizeResponseBehavior()
    {
        byte[] response = new byte[OcgCoreConstants.MaxResponseLength + 1];
        byte[] destination = Enumerable.Repeat((byte)0xCC, OcgCoreConstants.MaxResponseLength).ToArray();

        bool copied = NativeDuelSession.TryCopyResponse(response, destination);

        AssertFalse(copied);
        AssertSequenceEqual(Enumerable.Repeat((byte)0xCC, OcgCoreConstants.MaxResponseLength).ToArray(), destination);
    }

    [Fact]
    public void OcgCardDataMatchesNativeSize()
    {
        AssertEqual(80, Marshal.SizeOf<OcgCardData>());
    }

    [Fact]
    public void NativeDuelFactoryValidatesSeedSequenceLength()
    {
        using NativeOcgRuntime runtime = new();

        AssertThrows<ArgumentException>(() => runtime.DuelFactory.Create(new uint[1]));
    }

    [Fact]
    public void DeckRoutesMainExtraAndTokenCardsThroughRepository()
    {
        Deck deck = new(TestRepository(
            TestCard(1),
            TestCard(2, (int)CardType.Monster | (int)CardType.Fusion),
            TestCard(3, (int)CardType.Monster | (int)CardType.Token)));

        deck.AddMain(1);
        deck.AddMain(2);
        deck.AddMain(3);
        deck.AddMain(404);

        AssertCollectionEqual(new[] { 1 }, deck.Main);
        AssertCollectionEqual(new[] { 2 }, deck.Extra);
    }

    [Fact]
    public void DeckSideRejectsMissingAndTokenCards()
    {
        Deck deck = new(TestRepository(
            TestCard(1),
            TestCard(3, (int)CardType.Monster | (int)CardType.Token)));

        deck.AddSide(1);
        deck.AddSide(3);
        deck.AddSide(404);

        AssertCollectionEqual(new[] { 1 }, deck.Side);
    }

    [Fact]
    public void DeckCheckUsesRepositoryAliasesAndBanlist()
    {
        Deck deck = new(TestRepository(
            TestCard(10, alias: 20),
            TestCard(11, alias: 20)));
        Banlist banlist = new();
        banlist.Add(20, 1);

        deck.AddMain(10);
        deck.AddMain(11);

        AssertEqual(20, deck.Check(banlist, ocg: true, tcg: true, new DeckRules(MainDeckMinSize: 1)));
    }

    [Fact]
    public void RepositoryCardDataProviderMapsCardsToOcgData()
    {
        Card card = TestCard(
            100,
            (int)CardType.Monster | (int)CardType.Link,
            alias: 99,
            setcode: 0x1234,
            level: 7,
            lScale: 1,
            rScale: 2,
            race: (int)CardRace.Cyberse,
            attribute: (int)CardAttribute.Dark,
            attack: 2500,
            defense: 0,
            linkMarker: (int)CardLinkMarker.Bottom);
        RepositoryCardDataProvider provider = new(TestRepository(card));

        AssertTrue(provider.TryGetCardData(100, out OcgCardData data));
        AssertFalse(provider.TryGetCardData(404, out _));
        AssertEqual((uint)100, data.Code);
        AssertEqual((uint)99, data.Alias);
        AssertEqual((uint)card.Type, data.Type);
        AssertEqual((uint)7, data.Level);
        AssertEqual((uint)CardAttribute.Dark, data.Attribute);
        AssertEqual((uint)CardRace.Cyberse, data.Race);
        AssertEqual(2500, data.Attack);
        AssertEqual(0, data.Defense);
        AssertEqual((uint)1, data.LScale);
        AssertEqual((uint)2, data.RScale);
        AssertEqual((uint)CardLinkMarker.Bottom, data.LinkMarker);
    }

    [Fact]
    public void BanlistParserReadsNamesAndWhitelistMode()
    {
        string text = """
            # comment
            !TCG 2026
            1 0
            2	1
            $whitelist
            !OCG 2026
            3 2
            4 3
            """;

        List<Banlist> banlists = BanlistManager.ParseText(text);

        AssertEqual(2, banlists.Count);
        AssertEqual("TCG 2026", banlists[0].Name);
        AssertTrue(banlists[0].WhitelistOnly);
        AssertEqual(0, banlists[0].GetQuantity(1));
        AssertEqual(1, banlists[0].GetQuantity(2));
        AssertEqual(0, banlists[0].GetQuantity(99));
        AssertEqual("OCG 2026", banlists[1].Name);
        AssertEqual(2, banlists[1].GetQuantity(3));
        AssertEqual(3, banlists[1].GetQuantity(4));
    }

    [Fact]
    public void SqliteCardDatabaseManagerMapsCdbFields()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpCdbTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string firstDatabase = Path.Combine(temporaryDirectory, "cards1.cdb");
        string secondDatabase = Path.Combine(temporaryDirectory, "cards2.cdb");

        try
        {
            CreateTestCdb(firstDatabase, [
                TestCdbRow(1, (int)CardType.Monster | (int)CardType.Link, levelInfo: 4, defense: (int)CardLinkMarker.Bottom, name: "Link"),
                TestCdbRow(2, (int)CardType.Monster | (int)CardType.Pendulum, levelInfo: 4 | (8 << 16) | (1 << 24), defense: 2000, name: "Pendulum")
            ]);
            CreateTestCdb(secondDatabase, [
                TestCdbRow(3, (int)CardType.Monster, levelInfo: 7, defense: 2500, name: "Second")
            ]);

            SqliteCardDatabaseManager manager = new();
            ICardRepository cards = manager.LoadCards(firstDatabase);
            INamedCardRepository namedCards = manager.LoadNamedCards([firstDatabase, secondDatabase]);

            AssertTrue(cards.TryGetCard(1, out Card linkCard));
            AssertEqual(0, linkCard.Defense);
            AssertEqual((int)CardLinkMarker.Bottom, linkCard.LinkMarker);

            AssertTrue(cards.TryGetCard(2, out Card pendulumCard));
            AssertEqual(4, pendulumCard.Level);
            AssertEqual(1, pendulumCard.LScale);
            AssertEqual(8, pendulumCard.RScale);

            AssertTrue(namedCards.TryGetCard(1, out NamedCard firstNamedCard));
            AssertTrue(namedCards.TryGetCard(3, out NamedCard secondNamedCard));
            AssertEqual("Link", firstNamedCard.Name);
            AssertEqual("Second", secondNamedCard.Name);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void CoreMessageReaderSkipAdvancesAndBoundsChecks()
    {
        CoreMessage message = new(GameMessage.Hint, new byte[] { 10, 20, 30, 40, 50 });

        message.Reader.Skip(2);
        AssertEqual(2, message.Reader.Position);
        AssertEqual((byte)30, message.Reader.ReadByte());
        message.Reader.Skip(0);
        AssertEqual(3, message.Reader.Position);
        AssertSequenceEqual(new byte[] { 10, 20, 30 }, message.CreateBufferSpan());

        AssertThrows<EndOfStreamException>(() => message.Reader.Skip(3));
        AssertThrows<ArgumentOutOfRangeException>(() => message.Reader.Skip(-1));
    }

    [Fact]
    public void NamedCardRepositoryCachesAllCardsSnapshot()
    {
        InMemoryNamedCardRepository repository = TestNamedRepository(
            TestNamedCard(100, name: "Alpha"),
            TestNamedCard(200, name: "Beta"));

        IReadOnlyCollection<NamedCard> first = repository.GetAllCards();
        IReadOnlyCollection<NamedCard> second = repository.GetAllCards();

        AssertTrue(ReferenceEquals(first, second));
        AssertEqual(2, first.Count);
        AssertTrue(repository.TryGetCard(100, out NamedCard alpha));
        AssertEqual("Alpha", alpha.Name);
        AssertFalse(repository.TryGetCard(999, out _));
    }

    [Fact]
    public void BanlistQuantityLookupPreservesPrecedence()
    {
        Banlist banlist = new("TestList");
        banlist.Add(100, 3);
        banlist.Add(100, 2);
        banlist.Add(100, 0);
        banlist.Add(100, 1);

        banlist.Add(200, 2);
        banlist.Add(300, 1);

        AssertEqual(0, banlist.GetQuantity(100));
        AssertEqual(2, banlist.GetQuantity(200));
        AssertEqual(1, banlist.GetQuantity(300));
        AssertEqual(3, banlist.GetQuantity(400));

        banlist.EnableWhitelistMode();
        AssertEqual(0, banlist.GetQuantity(400));
        AssertEqual(2, banlist.GetQuantity(200));
    }

    [Fact]
    public void CoreYdkPresetPoolAndRandomDeckGeneratorEnforceRulesAndMirrorSymmetry()
    {
        List<Card> poolCards = new();
        // 15 种下级怪兽
        for (int i = 1; i <= 15; i++)
            poolCards.Add(TestCard(1000 + i, (int)CardType.Monster | (i <= 3 ? (int)CardType.Tuner : (int)CardType.Effect), level: 4, attack: 1800, defense: 1000));
        // 5 种上级怪兽
        for (int i = 1; i <= 5; i++)
            poolCards.Add(TestCard(2000 + i, (int)CardType.Monster | (int)CardType.Effect, level: 7, attack: 2600, defense: 2000));
        // 8 种魔法 + 6 种陷阱
        for (int i = 1; i <= 8; i++)
            poolCards.Add(TestCard(3000 + i, (int)CardType.Spell));
        for (int i = 1; i <= 6; i++)
            poolCards.Add(TestCard(4000 + i, (int)CardType.Trap));
        // 额外卡组（融合/同调/超量/连接）
        for (int i = 1; i <= 3; i++)
            poolCards.Add(TestCard(5000 + i, (int)CardType.Monster | (int)CardType.Fusion, level: 8));
        for (int i = 1; i <= 3; i++)
            poolCards.Add(TestCard(5100 + i, (int)CardType.Monster | (int)CardType.Synchro, level: 8));
        for (int i = 1; i <= 3; i++)
            poolCards.Add(TestCard(5200 + i, (int)CardType.Monster | (int)CardType.Xyz, level: 4));
        for (int i = 1; i <= 3; i++)
            poolCards.Add(TestCard(5300 + i, (int)CardType.Monster | (int)CardType.Link, level: 2));

        // 边界特殊卡：衍生物（必须排除）、禁卡（7001）、限一异画同名卡（7002 与 alias=7002 的 7003）、DIY 卡（ot=4）
        poolCards.Add(TestCard(7000, (int)CardType.Monster | (int)CardType.Token));
        poolCards.Add(TestCard(7001, (int)CardType.Spell));
        poolCards.Add(TestCard(7002, (int)CardType.Spell));
        poolCards.Add(TestCard(7003, (int)CardType.Spell, alias: 7002));
        for (int i = 1; i <= 5; i++)
            poolCards.Add(new Card(99000 + i, ot: 4, alias: 0, setcode: 0, type: (int)CardType.Monster | (int)CardType.Effect, level: 4, lScale: 0, rScale: 0, race: 1, attribute: 1, attack: 2100, defense: 1500));

        InMemoryCardRepository repo = new(poolCards);
        Banlist ban = new("ChaosBan");
        ban.Add(7001, 0); // 禁止
        ban.Add(7002, 1); // 限制（含异画 7003 合计最多 1 张）

        // 1. YdkSerializer 往返解析与序列化
        string sampleYdk = "#created by test\n#main\n1001\n3001\n#extra\n5001\n!side\n4001\n";
        YdkDeckData parsedYdk = YdkSerializer.Parse(sampleYdk, "Sample", ["meta"]);
        AssertEqual("Sample", parsedYdk.Name);
        AssertCollectionEqual(new[] { 1001, 3001 }, parsedYdk.Main);
        AssertCollectionEqual(new[] { 5001 }, parsedYdk.Extra);
        AssertCollectionEqual(new[] { 4001 }, parsedYdk.Side);
        Deck convertedDeck = YdkSerializer.ToDeck(parsedYdk, repo);
        YdkDeckData reparsed = YdkSerializer.Parse(YdkSerializer.Serialize(convertedDeck), "Sample2");
        AssertCollectionEqual(new[] { 1001, 3001 }, reparsed.Main);
        AssertCollectionEqual(new[] { 5001 }, reparsed.Extra);
        AssertCollectionEqual(new[] { 4001 }, reparsed.Side);

        // 2. ChaosDraft 40+15 配比、禁限表、异画同名上限、衍生物与 DIY 区域过滤
        PresetDeckPool presetPool = new();
        RandomDeckGenerator generator = new(repo, presetPool);
        GeneratedRandomDeck chaos = generator.GenerateSingle(
            RandomDeckMode.ChaosDraft,
            ban,
            ocg: true,
            tcg: true,
            allowCustom: false,
            DeckRules.Default,
            random: new Random(42));

        AssertEqual(40, chaos.Deck.Main.Count);
        AssertEqual(15, chaos.Deck.Extra.Count);
        AssertFalse(chaos.Deck.Main.Contains(7000)); // 无 Token
        AssertFalse(chaos.Deck.Main.Contains(7001)); // 无禁卡
        AssertTrue(chaos.Deck.Main.Count(id => id == 7002 || id == 7003) <= 1); // 异画同名受限一约束
        AssertFalse(chaos.Deck.Main.Any(id => id >= 99000)); // allowCustom=false 时不混入 DIY 卡
        AssertEqual(0, chaos.Deck.Check(ban, ocg: true, tcg: true, allowCustom: false, DeckRules.Default));
        AssertTrue(chaos.Deck.Main.Any(id => id is > 1000 and < 2000)); // 含下级怪
        AssertTrue(chaos.Deck.Main.Any(id => id is > 3000 and < 4000)); // 含魔法
        AssertTrue(chaos.Deck.Main.Any(id => id is > 4000 and < 5000)); // 含陷阱

        // 3. DiyChaos 优先抽取 ot=4 DIY 卡片
        GeneratedRandomDeck diyChaos = generator.GenerateSingle(
            RandomDeckMode.DiyChaos,
            ban,
            ocg: true,
            tcg: true,
            allowCustom: false,
            DeckRules.Default,
            random: new Random(7));
        AssertTrue(diyChaos.Deck.Main.Count(id => id >= 99000) >= 10);

        // 4. 预设池与 1v1 / 2v2 MirrorRandom 对称镜像分配
        YdkDeckData presetA = new("DeckAlpha", Enumerable.Range(1, 15).SelectMany(i => new[] { 1000 + i, 1000 + i }).Concat(Enumerable.Repeat(3001, 3)).Concat(Enumerable.Repeat(3002, 3)).Concat(Enumerable.Repeat(4001, 4)).ToArray(), [5001, 5101, 5201, 5301], [], ["meta"]);
        YdkDeckData presetB = new("DeckBeta", Enumerable.Range(1, 15).SelectMany(i => new[] { 1000 + i, 1000 + i }).Concat(Enumerable.Repeat(3003, 3)).Concat(Enumerable.Repeat(3004, 3)).Concat(Enumerable.Repeat(4002, 4)).ToArray(), [5002, 5102, 5202, 5302], [], ["fun"]);
        presetPool.AddOrUpdate(new PresetDeckEntry("meta/DeckAlpha", "DeckAlpha", null, presetA, HasDiyCards: false));
        presetPool.AddOrUpdate(new PresetDeckEntry("fun/DeckBeta", "DeckBeta", null, presetB, HasDiyCards: false));

        IReadOnlyList<GeneratedRandomDeck> mirror1v1 = generator.AllocateDecksForDuel(
            2, RandomDeckMode.MirrorRandom, ban, ocg: true, tcg: true, allowCustom: false, DeckRules.Default, random: new Random(100));
        AssertEqual(2, mirror1v1.Count);
        AssertTrue(mirror1v1[0].Deck.Check(mirror1v1[1].Deck));

        IReadOnlyList<GeneratedRandomDeck> mirror2v2 = generator.AllocateDecksForDuel(
            4, RandomDeckMode.MirrorRandom, ban, ocg: true, tcg: true, allowCustom: false, DeckRules.Default, random: new Random(100));
        AssertEqual(4, mirror2v2.Count);
        AssertTrue(mirror2v2[0].Deck.Check(mirror2v2[2].Deck)); // A队#0 与 B队#2 同构筑
        AssertTrue(mirror2v2[1].Deck.Check(mirror2v2[3].Deck)); // A队#1 与 B队#3 同构筑
        AssertFalse(mirror2v2[0].Deck.Check(mirror2v2[1].Deck)); // 同队搭档拿到两套不同卡组
    }

}
