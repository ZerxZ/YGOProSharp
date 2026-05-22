using System.Buffers.Binary;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;
using YGOProSharp.NativeApi;
using YGOProSharp.Server;

ListLoggerProvider logProvider = TestLog.Provider;
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddProvider(logProvider);
    builder.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "";
    });
});
AppLog.Configure(loggerFactory);
ILogger testLogger = AppLog.CreateLogger("YGOProSharp.Tests");

Run("AppLog parses log levels", AppLogParsesLogLevels);
Run("CoreMessage reads native payloads from spans", CoreMessageReadsNativePayloadsFromSpans);
Run("Native duel rejects too-small query destination buffers", NativeDuelRejectsTooSmallQueryDestinationBuffers);
Run("Native duel preserves oversize response behavior", NativeDuelPreservesOversizeResponseBehavior);
Run("OcgCardData matches native card_data size", OcgCardDataMatchesNativeSize);
Run("Native duel factory validates seed sequence length", NativeDuelFactoryValidatesSeedSequenceLength);
Run("Deck routes main, extra, and token cards through repository", DeckRoutesMainExtraAndTokenCardsThroughRepository);
Run("Deck side rejects missing and token cards", DeckSideRejectsMissingAndTokenCards);
Run("Deck check uses repository aliases and banlist", DeckCheckUsesRepositoryAliasesAndBanlist);
Run("Repository card data provider maps cards to ocg data", RepositoryCardDataProviderMapsCardsToOcgData);
Run("Project boundaries keep native interop out of core", ProjectBoundariesKeepNativeInteropOutOfCore);
Run("Project boundaries keep card models and sqlite separated", ProjectBoundariesKeepCardModelsAndSqliteSeparated);
Run("Project boundaries split core protocol and server", ProjectBoundariesSplitCoreProtocolAndServer);
Run("Project boundaries keep WindBot decoupled", ProjectBoundariesKeepWindBotDecoupled);
Run("Project boundaries keep direct Console writes out of source", ProjectBoundariesKeepDirectConsoleWritesOutOfSource);
Run("Project boundaries keep logger parameters out of business APIs", ProjectBoundariesKeepLoggerParametersOutOfBusinessApis);
Run("WindBot packet factory writes CTOS message byte", WindBotPacketFactoryWritesCtosMessageByte);
Run("WindBot deck loads through named-card repository", WindBotDeckLoadsThroughNamedCardRepository);
Run("WindBot server mode parses query info", WindBotServerModeParsesQueryInfo);
Run("WindBot bot list path logs missing file", WindBotBotListPathLogsMissingFile);
Run("Protocol message enums include reference values", ProtocolMessageEnumsIncludeReferenceValues);
Run("PacketFramer handles split and sticky packets", PacketFramerHandlesSplitAndStickyPackets);
Run("PacketFramer handles 4-byte size-included headers", PacketFramerHandlesFourByteSizeIncludedHeaders);
Run("PacketFramer rejects oversize packets", PacketFramerRejectsOversizePackets);
Run("PacketReader reads little-endian and UTF-16 data", PacketReaderReadsLittleEndianAndUtf16Data);
Run("Binary extensions read and write fixed UTF-8 strings", BinaryExtensionsReadAndWriteFixedUtf8Strings);
Run("Banlist parser reads names and whitelist mode", BanlistParserReadsNamesAndWhitelistMode);
Run("Sqlite card database manager maps cdb fields", SqliteCardDatabaseManagerMapsCdbFields);
Run("HostInfo parses CreateGame payload", HostInfoParsesCreateGamePayload);
Run("GamePacketFactory creates deck count and field finish", GamePacketFactoryCreatesDeckCountAndFieldFinish);
Run("Player handles request field safely", PlayerHandlesRequestFieldSafely);
Run("Player parses player info from spans", PlayerParsesPlayerInfoFromSpans);
Run("Game logs native errors", GameLogsNativeErrors);
await RunAsync("NetworkClient loopback send and receive", NetworkClientLoopbackSendAndReceiveAsync);

static void AppLogParsesLogLevels()
{
    AssertEqual((int)LogLevel.Information, (int)AppLog.ParseLevel(null));
    AssertEqual((int)LogLevel.Debug, (int)AppLog.ParseLevel("Debug"));
    AssertEqual((int)LogLevel.Trace, (int)AppLog.ParseLevel("trace"));
    AssertEqual((int)LogLevel.Warning, (int)AppLog.ParseLevel("nope", LogLevel.Warning));
}

static void CoreMessageReadsNativePayloadsFromSpans()
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

static void NativeDuelRejectsTooSmallQueryDestinationBuffers()
{
    byte[] source = [1, 2, 3, 4];
    byte[] destination = new byte[3];

    AssertThrows<ArgumentException>(() => NativeDuelSession.CopyQueryResult(source, source.Length, destination, "test_query"));
}

static void NativeDuelPreservesOversizeResponseBehavior()
{
    byte[] response = new byte[OcgCoreConstants.MaxResponseLength + 1];
    byte[] destination = Enumerable.Repeat((byte)0xCC, OcgCoreConstants.MaxResponseLength).ToArray();

    bool copied = NativeDuelSession.TryCopyResponse(response, destination);

    AssertFalse(copied);
    AssertSequenceEqual(Enumerable.Repeat((byte)0xCC, OcgCoreConstants.MaxResponseLength).ToArray(), destination);
}

static void OcgCardDataMatchesNativeSize()
{
    AssertEqual(80, Marshal.SizeOf<OcgCardData>());
}

static void NativeDuelFactoryValidatesSeedSequenceLength()
{
    using NativeOcgRuntime runtime = new();

    AssertThrows<ArgumentException>(() => runtime.DuelFactory.Create(new uint[1]));
}

static void DeckRoutesMainExtraAndTokenCardsThroughRepository()
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

static void DeckSideRejectsMissingAndTokenCards()
{
    Deck deck = new(TestRepository(
        TestCard(1),
        TestCard(3, (int)CardType.Monster | (int)CardType.Token)));

    deck.AddSide(1);
    deck.AddSide(3);
    deck.AddSide(404);

    AssertCollectionEqual(new[] { 1 }, deck.Side);
}

static void DeckCheckUsesRepositoryAliasesAndBanlist()
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

static void RepositoryCardDataProviderMapsCardsToOcgData()
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

static void ProjectBoundariesKeepNativeInteropOutOfCore()
{
    string root = FindRepositoryRoot();
    string coreProject = Path.Combine(root, "YGOProSharp.Core");
    string abstractionsProject = Path.Combine(root, "YGOProSharp.Abstractions", "YGOProSharp.Abstractions.csproj");

    string[] forbiddenCoreTokens = ["LibraryImport", "DllImport", "SafeHandle", "byte*", "OcgCoreImports"];
    foreach (string file in Directory.EnumerateFiles(coreProject, "*.cs", SearchOption.AllDirectories)
                 .Where(IsRepositorySourcePath))
    {
        string text = File.ReadAllText(file);
        foreach (string token in forbiddenCoreTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden native interop token {token}.");
        }
    }

    string abstractionsXml = File.ReadAllText(abstractionsProject);
    foreach (string forbiddenReference in new[] { "YGOProSharp.Native", "YGOProSharp.NativeApi", "Microsoft.Data.Sqlite", "SevenZip" })
    {
        if (abstractionsXml.Contains(forbiddenReference, StringComparison.Ordinal))
            throw new InvalidOperationException($"YGOProSharp.Abstractions references {forbiddenReference}.");
    }
}

static void ProjectBoundariesKeepCardModelsAndSqliteSeparated()
{
    string root = FindRepositoryRoot();
    string coreProject = Path.Combine(root, "YGOProSharp.Core");
    string sqliteManagerPath = Path.Combine(coreProject, "Cards", "SqliteCardDatabaseManager.cs");
    string[] cardModelFiles =
    [
        Path.Combine(coreProject, "Cards", "Card.cs"),
        Path.Combine(coreProject, "Cards", "NamedCard.cs")
    ];

    string[] forbiddenCardModelTokens = ["IDataRecord", "Microsoft.Data.Sqlite", "OcgCardData", "CardsManager"];
    foreach (string file in cardModelFiles)
    {
        string text = File.ReadAllText(file);
        foreach (string token in forbiddenCardModelTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden card model dependency {token}.");
        }
    }

    string[] forbiddenSqliteTokens = ["Microsoft.Data.Sqlite", "SqliteConnection"];
    foreach (string file in Directory.EnumerateFiles(coreProject, "*.cs", SearchOption.AllDirectories)
                 .Where(file => IsRepositorySourcePath(file) &&
                                !Path.GetFullPath(file).Equals(sqliteManagerPath, StringComparison.OrdinalIgnoreCase)))
    {
        string text = File.ReadAllText(file);
        foreach (string token in forbiddenSqliteTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden direct sqlite dependency {token}.");
        }
    }

    string[] forbiddenStaticLookups = ["Card." + "Get(", "NamedCard." + "Get("];
    foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                 .Where(IsRepositorySourcePath))
    {
        string text = File.ReadAllText(file);
        foreach (string token in forbiddenStaticLookups)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden static card lookup.");
        }
    }
}

static void ProjectBoundariesSplitCoreProtocolAndServer()
{
    string root = FindRepositoryRoot();
    string coreProject = Path.Combine(root, "YGOProSharp.Core");
    string protocolProject = Path.Combine(root, "YGOProSharp.Protocol");
    string serverProject = Path.Combine(root, "YGOProSharp.Server");

    AssertDoesNotContainAny(
        ProjectText(coreProject),
        ["YGOProSharp.Protocol", "YGOProSharp.Server", "SevenZip", "YGOClient", "CtosMessage", "StocMessage"],
        "Core project boundary");

    AssertDoesNotContainAny(
        ProjectText(protocolProject),
        ["YGOProSharp.Core", "YGOProSharp.Server", "YGOProSharp.NativeApi", "Microsoft.Data.Sqlite", "SqliteConnection"],
        "Protocol project boundary");

    string serverProjectFile = File.ReadAllText(Path.Combine(serverProject, "YGOProSharp.Server.csproj"));
    foreach (string requiredReference in new[] { "YGOProSharp.Core", "YGOProSharp.Protocol", "YGOProSharp.NativeApi" })
    {
        if (!serverProjectFile.Contains(requiredReference, StringComparison.Ordinal))
            throw new InvalidOperationException($"Server project must reference {requiredReference}.");
    }
}

static void ProjectBoundariesKeepWindBotDecoupled()
{
    string root = FindRepositoryRoot();
    string windBotProject = Path.Combine(root, "YGOProSharp.WindBot");

    string sourceText = ProjectText(windBotProject);
    string consolePrefix = "Console.";
    string consoleErrorPrefix = consolePrefix + "Error.";
    AssertDoesNotContainAny(
        sourceText,
        ["YGOSharp", "MDPro3", "YGOProSharp.Server", "YGOProSharp.NativeApi", consolePrefix + "WriteLine", consoleErrorPrefix + "WriteLine"],
        "WindBot project boundary");

    string projectFile = File.ReadAllText(Path.Combine(windBotProject, "YGOProSharp.WindBot.csproj"));
    foreach (string requiredReference in new[] { "YGOProSharp.Abstractions", "YGOProSharp.Core", "YGOProSharp.Protocol" })
    {
        if (!projectFile.Contains(requiredReference, StringComparison.Ordinal))
            throw new InvalidOperationException($"WindBot project must reference {requiredReference}.");
    }
}

static void WindBotPacketFactoryWritesCtosMessageByte()
{
    using BinaryWriter writer = WindBot.Game.GamePacketFactory.Create(CtosMessage.Chat);
    MemoryStream stream = (MemoryStream)writer.BaseStream;

    AssertSequenceEqual(new[] { (byte)CtosMessage.Chat }, stream.ToArray());
}

static void WindBotDeckLoadsThroughNamedCardRepository()
{
    string previousDirectory = Environment.CurrentDirectory;
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotDeckTest_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

    try
    {
        File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "Smoke.ydk"),
        [
            "#created by test",
            "1",
            "2",
            "!side",
            "3",
            "404"
        ]);

        Environment.CurrentDirectory = temporaryDirectory;
        WindBot.Game.Deck deck = WindBot.Game.Deck.Load("Smoke", TestNamedRepository(
            TestNamedCard(1),
            TestNamedCard(2, (int)CardType.Monster | (int)CardType.Fusion),
            TestNamedCard(3)));

        AssertEqual(1, deck.Cards.Count);
        AssertEqual(1, deck.ExtraCards.Count);
        AssertEqual(1, deck.SideCards.Count);
        AssertEqual(1, deck.Cards[0].Id);
        AssertEqual(2, deck.ExtraCards[0].Id);
        AssertEqual(3, deck.SideCards[0].Id);
    }
    finally
    {
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static void WindBotServerModeParsesQueryInfo()
{
    NameValueCollection query = WindBot.QueryStringParser.ParseQueryString(
        "name=Bot&deck=AI&host=127.0.0.1&port=7911&deckfile=Smoke.ydk&dialog=default&version=4962&password=abc&hand=1&debug=true&chat=false");

    WindBot.WindBotInfo info = WindBot.WindBotServerModeHost.CreateInfoFromQuery(query);
    if (info == null)
        throw new InvalidOperationException("Expected query to create WindBotInfo.");

    AssertEqual("Bot", info.Name);
    AssertEqual("AI", info.Deck);
    AssertEqual("127.0.0.1", info.Host);
    AssertEqual(7911, info.Port);
    AssertEqual("Smoke.ydk", info.DeckFile);
    AssertEqual("default", info.Dialog);
    AssertEqual(4962, info.Version);
    AssertEqual("abc", info.HostInfo);
    AssertEqual(1, info.Hand);
    AssertTrue(info.Debug);
    AssertFalse(info.Chat);
}

static void WindBotBotListPathLogsMissingFile()
{
    TestLog.Provider.Clear();
    string missingPath = Path.Combine(Path.GetTempPath(), "missing_windbot_list_" + Guid.NewGuid().ToString("N") + ".json");

    IReadOnlyList<WindBot.WindBotInfo> bots = WindBot.WindBotServerModeHost.LoadBotList(missingPath);

    AssertEqual(0, bots.Count);
    AssertTrue(TestLog.Provider.Contains(LogLevel.Error, "BotListPath file not found"));
}

static void ProtocolMessageEnumsIncludeReferenceValues()
{
    AssertEqual(0x30, (int)CtosMessage.RequestField);
    AssertEqual(0x09, (int)StocMessage.DeckCount);
    AssertEqual(0x30, (int)StocMessage.FieldFinish);
    AssertEqual(0x31, (int)StocMessage.SrvproRoomlist);
}

static void ProjectBoundariesKeepDirectConsoleWritesOutOfSource()
{
    string root = FindRepositoryRoot();
    string consolePrefix = "Console.";
    string consoleErrorPrefix = consolePrefix + "Error.";
    string[] forbiddenWrites =
    [
        consolePrefix + "WriteLine",
        consoleErrorPrefix + "WriteLine",
        consolePrefix + "Write(",
        consoleErrorPrefix + "Write("
    ];

    foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                 .Where(IsRepositorySourcePath))
    {
        string text = File.ReadAllText(file);
        foreach (string forbiddenWrite in forbiddenWrites)
        {
            if (text.Contains(forbiddenWrite, StringComparison.Ordinal))
                throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} contains forbidden direct console write {forbiddenWrite}.");
        }
    }
}

static void ProjectBoundariesKeepLoggerParametersOutOfBusinessApis()
{
    string root = FindRepositoryRoot();
    string[] businessProjects =
    [
        Path.Combine(root, "YGOProSharp.Core"),
        Path.Combine(root, "YGOProSharp.Protocol"),
        Path.Combine(root, "YGOProSharp.Server")
    ];
    string appLogPath = Path.Combine(root, "YGOProSharp.Abstractions", "Logging", "AppLog.cs");
    string[] forbiddenParameterTokens = ["ILogger ", "ILogger<", "ILoggerFactory"];

    foreach (string file in businessProjects.SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
                 .Where(file => IsRepositorySourcePath(file) &&
                                !Path.GetFullPath(file).Equals(appLogPath, StringComparison.OrdinalIgnoreCase)))
    {
        foreach (string line in File.ReadLines(file))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("public ", StringComparison.Ordinal))
                continue;
            if (!trimmed.Contains('('))
                continue;

            foreach (string token in forbiddenParameterTokens)
            {
                if (trimmed.Contains(token, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{Path.GetRelativePath(root, file)} exposes logger parameter in public API.");
            }
        }
    }
}

static void PacketFramerHandlesSplitAndStickyPackets()
{
    PacketFramer framer = new();
    byte[] first = framer.Frame([1, 2, 3]);
    byte[] second = framer.Frame([4, 5]);

    framer.Append(first.AsSpan(0, 1));
    AssertFalse(framer.TryReadPacket(out _));

    framer.Append(first.AsSpan(1));
    AssertTrue(framer.TryReadPacket(out byte[] firstPacket));
    AssertSequenceEqual(new byte[] { 1, 2, 3 }, firstPacket);

    byte[] sticky = [.. second, .. framer.Frame([6])];
    framer.Append(sticky);

    AssertTrue(framer.TryReadPacket(out byte[] secondPacket));
    AssertSequenceEqual(new byte[] { 4, 5 }, secondPacket);

    AssertTrue(framer.TryReadPacket(out byte[] thirdPacket));
    AssertSequenceEqual(new byte[] { 6 }, thirdPacket);
}

static void PacketFramerHandlesFourByteSizeIncludedHeaders()
{
    PacketFramer framer = new(headerSize: 4, isHeaderSizeIncluded: true);
    byte[] frame = framer.Frame([7, 8]);

    AssertEqual(6, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0, 4)));

    framer.Append(frame);
    AssertTrue(framer.TryReadPacket(out byte[] packet));
    AssertSequenceEqual(new byte[] { 7, 8 }, packet);
}

static void PacketFramerRejectsOversizePackets()
{
    PacketFramer framer = new(maxPacketLength: 4);
    Span<byte> header = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(header, 5);

    framer.Append(header);

    AssertThrows<InvalidDataException>(() => framer.TryReadPacket(out _));
}

static void PacketReaderReadsLittleEndianAndUtf16Data()
{
    byte[] data = new byte[2 + 4 + 8 + 2];
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0, 2), 0x1234);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2, 4), 0x12345678);
    Encoding.Unicode.GetBytes("Hi\0\0", data.AsSpan(6, 8));
    data[^2] = 0xAA;
    data[^1] = 0xBB;

    PacketReader reader = new(data);

    AssertEqual(0x1234, reader.ReadInt16());
    AssertEqual(0x12345678, reader.ReadInt32());
    AssertEqual("Hi", reader.ReadUnicode(4));
    AssertSequenceEqual(new byte[] { 0xAA, 0xBB }, reader.ReadRemainingBytes());
    AssertEqual(0, reader.Remaining);

    PacketReader emptyReader = new(new byte[4]);
    AssertEqual("", emptyReader.ReadUnicode(2));
}

static void BinaryExtensionsReadAndWriteFixedUtf8Strings()
{
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.WriteUtf8("room", 8);
    writer.WriteUtf8("大厅", 12);

    stream.Position = 0;
    using BinaryReader reader = new(stream);

    AssertEqual("room", reader.ReadUtf8(8));
    AssertEqual("大厅", reader.ReadUtf8(12));

    PacketReader packetReader = new(stream.ToArray());
    AssertEqual("room", packetReader.ReadUtf8(8));
    AssertEqual("大厅", packetReader.ReadUtf8(12));
}

static void BanlistParserReadsNamesAndWhitelistMode()
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

static void SqliteCardDatabaseManagerMapsCdbFields()
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

static void HostInfoParsesCreateGamePayload()
{
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.Write(0x12345678U);
    writer.Write((byte)2);
    writer.Write((byte)5);
    writer.Write((byte)1);
    writer.Write((byte)1);
    writer.Write((byte)0);
    writer.Write((byte)1);
    writer.Write(new byte[3]);
    writer.Write(4000);
    writer.Write((byte)4);
    writer.Write((byte)2);
    writer.Write((short)120);

    stream.Position = 0;
    using BinaryReader reader = new(stream);
    HostInfo info = HostInfo.ReadFrom(reader);

    AssertEqual(0x12345678U, info.LfList);
    AssertEqual(2, info.Region);
    AssertEqual(5, info.MasterRule);
    AssertEqual(1, info.Mode);
    AssertTrue(info.EnablePriority);
    AssertFalse(info.NoCheckDeck);
    AssertTrue(info.NoShuffleDeck);
    AssertEqual(4000, info.StartLp);
    AssertEqual(4, info.StartHand);
    AssertEqual(2, info.DrawCount);
    AssertEqual(120, info.TimeLimit);
}

static void GamePacketFactoryCreatesDeckCountAndFieldFinish()
{
    using BinaryWriter deckCount = GamePacketFactory.CreateDeckCount(40, 15, 3, 41, 14, 2);
    byte[] deckCountBytes = ((MemoryStream)deckCount.BaseStream).ToArray();

    AssertEqual(13, deckCountBytes.Length);
    AssertEqual((byte)StocMessage.DeckCount, deckCountBytes[0]);
    AssertEqual(40, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(1, 2)));
    AssertEqual(15, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(3, 2)));
    AssertEqual(3, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(5, 2)));
    AssertEqual(41, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(7, 2)));
    AssertEqual(14, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(9, 2)));
    AssertEqual(2, (int)BinaryPrimitives.ReadInt16LittleEndian(deckCountBytes.AsSpan(11, 2)));

    using BinaryWriter fieldFinish = GamePacketFactory.CreateFieldFinish();
    AssertSequenceEqual(new[] { (byte)StocMessage.FieldFinish }, ((MemoryStream)fieldFinish.BaseStream).ToArray());
}

static void PlayerHandlesRequestFieldSafely()
{
    TestLog.Provider.Clear();
    Config.Load([]);
    Game game = new(new CoreServer());
    Player player = new(game, new YGOClient());

    using MemoryStream infoStream = new();
    using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)CtosMessage.PlayerInfo);
        writer.WriteUnicode("FieldTester", 20);
    }
    player.Parse(infoStream.ToArray());

    using MemoryStream joinStream = new();
    using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)CtosMessage.JoinGame);
        writer.Write((short)0x1362);
        writer.Write(0);
        writer.Write((short)0);
    }
    player.Parse(joinStream.ToArray());
    player.Parse(new[] { (byte)CtosMessage.RequestField });

    AssertTrue(TestLog.Provider.Contains(LogLevel.Debug, "Ignoring field request"));
}

static void PlayerParsesPlayerInfoFromSpans()
{
    TestLog.Provider.Clear();
    Config.Load([]);
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.Write((byte)CtosMessage.PlayerInfo);
    writer.WriteUnicode("Tester", 20);

    Player player = new(new Game(new CoreServer()), new YGOClient());

    player.Parse(stream.ToArray());

    AssertEqual("Tester", player.Name);
    AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Player identified as Tester."));
}

static void GameLogsNativeErrors()
{
    TestLog.Provider.Clear();
    string previousCurrentDirectory = Directory.GetCurrentDirectory();
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpTests_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporaryDirectory);

    try
    {
        Directory.SetCurrentDirectory(temporaryDirectory);
        Game game = new(new CoreServer());
        System.Reflection.MethodInfo handleError = typeof(Game).GetMethod(
            "HandleError",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(Game), "HandleError");

        handleError.Invoke(game, ["synthetic lua error"]);

        AssertTrue(Directory.EnumerateFiles(temporaryDirectory, "lua_*.txt").Any());
        AssertTrue(TestLog.Provider.Contains(LogLevel.Error, "Native/Lua error"));
    }
    finally
    {
        Directory.SetCurrentDirectory(previousCurrentDirectory);
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static async Task NetworkClientLoopbackSendAndReceiveAsync()
{
    TestLog.Provider.Clear();
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();

    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using NetworkClient client = new();
    TaskCompletionSource<byte[]> clientReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    client.DataReceived += data => clientReceived.TrySetResult(data);

    Task<Socket> acceptTask = listener.AcceptSocketAsync();
    await client.ConnectAsync(IPAddress.Loopback, port);

    using Socket serverSocket = await acceptTask;
    await serverSocket.SendAsync(new byte[] { 1, 2, 3 });

    byte[] receivedByClient = await WaitAsync(clientReceived.Task);
    AssertSequenceEqual(new byte[] { 1, 2, 3 }, receivedByClient);

    await client.SendAsync(new byte[] { 4, 5 });
    byte[] serverBuffer = new byte[2];
    int bytesRead = await serverSocket.ReceiveAsync(serverBuffer);

    AssertEqual(2, bytesRead);
    AssertSequenceEqual(new byte[] { 4, 5 }, serverBuffer);

    client.Close();
    listener.Stop();

    AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Network client connected"));
    AssertTrue(TestLog.Provider.Contains(LogLevel.Debug, "Received 3 bytes"));
    AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Network client disconnected"));
}

void Run(string name, Action test)
{
    try
    {
        test();
        testLogger.LogInformation("PASS {TestName}", name);
    }
    catch (Exception ex)
    {
        testLogger.LogError(ex, "FAIL {TestName}", name);
        Environment.ExitCode = 1;
    }
}

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        testLogger.LogInformation("PASS {TestName}", name);
    }
    catch (Exception ex)
    {
        testLogger.LogError(ex, "FAIL {TestName}", name);
        Environment.ExitCode = 1;
    }
}

static void AssertEqual<T>(T expected, T actual)
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
        throw new InvalidOperationException($"Expected {expected}, actual {actual}.");
}

static void AssertFalse(bool value)
{
    if (value)
        throw new InvalidOperationException("Expected false.");
}

static void AssertTrue(bool value)
{
    if (!value)
        throw new InvalidOperationException("Expected true.");
}

static void AssertSequenceEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expected.ToArray())}], actual [{string.Join(", ", actual.ToArray())}].");
}

static void AssertCollectionEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    where T : IEquatable<T>
{
    T[] expectedItems = expected.ToArray();
    T[] actualItems = actual.ToArray();
    if (!expectedItems.SequenceEqual(actualItems))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expectedItems)}], actual [{string.Join(", ", actualItems)}].");
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static async Task<T> WaitAsync<T>(Task<T> task)
{
    Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
    if (completed != task)
        throw new TimeoutException("Timed out waiting for asynchronous test operation.");

    return await task;
}

static Card TestCard(
    int id,
    int type = (int)CardType.Monster,
    int alias = 0,
    long setcode = 0,
    int ot = 3,
    int level = 4,
    int lScale = 0,
    int rScale = 0,
    int race = (int)CardRace.Warrior,
    int attribute = (int)CardAttribute.Earth,
    int attack = 1000,
    int defense = 1000,
    int linkMarker = 0)
{
    return new Card(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker);
}

static InMemoryCardRepository TestRepository(params Card[] cards)
{
    return new InMemoryCardRepository(cards);
}

static NamedCard TestNamedCard(
    int id,
    int type = (int)CardType.Monster,
    int alias = 0,
    long setcode = 0,
    int ot = 3,
    int level = 4,
    int lScale = 0,
    int rScale = 0,
    int race = (int)CardRace.Warrior,
    int attribute = (int)CardAttribute.Earth,
    int attack = 1000,
    int defense = 1000,
    int linkMarker = 0,
    string? name = null,
    string description = "")
{
    return new NamedCard(id, ot, alias, setcode, type, level, lScale, rScale, race, attribute, attack, defense, linkMarker, name ?? $"Card {id}", description);
}

static InMemoryNamedCardRepository TestNamedRepository(params NamedCard[] cards)
{
    return new InMemoryNamedCardRepository(cards);
}

static TestCdbRow TestCdbRow(int id, int type, int levelInfo, int defense, string name)
{
    return new TestCdbRow(id, type, levelInfo, defense, name);
}

static void CreateTestCdb(string path, IReadOnlyList<TestCdbRow> rows)
{
    using SqliteConnection connection = new($"Data Source={path}");
    connection.Open();

    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE datas(id INTEGER, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, level INTEGER, race INTEGER, attribute INTEGER, atk INTEGER, def INTEGER);
        CREATE TABLE texts(id INTEGER, name TEXT, desc TEXT);
        """;
    command.ExecuteNonQuery();

    foreach (TestCdbRow row in rows)
    {
        command.CommandText = """
            INSERT INTO datas(id, ot, alias, setcode, type, level, race, attribute, atk, def)
            VALUES ($id, 3, 0, 0, $type, $level, $race, $attribute, 1000, $def);
            INSERT INTO texts(id, name, desc)
            VALUES ($id, $name, $desc);
            """;
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$type", row.Type);
        command.Parameters.AddWithValue("$level", row.LevelInfo);
        command.Parameters.AddWithValue("$race", (int)CardRace.Warrior);
        command.Parameters.AddWithValue("$attribute", (int)CardAttribute.Earth);
        command.Parameters.AddWithValue("$def", row.Defense);
        command.Parameters.AddWithValue("$name", row.Name);
        command.Parameters.AddWithValue("$desc", row.Name + " desc");
        command.ExecuteNonQuery();
    }
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "YGOProSharp.slnx")))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find repository root.");
}

static string ProjectText(string projectDirectory)
{
    IEnumerable<string> files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
        .Concat(Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
        .Where(IsRepositorySourcePath);

    return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
}

static bool IsRepositorySourcePath(string file)
{
    string fullPath = Path.GetFullPath(file);
    string separator = Path.DirectorySeparatorChar.ToString();
    string normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    return !normalized.Contains(separator + "bin" + separator, StringComparison.OrdinalIgnoreCase) &&
           !normalized.Contains(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase) &&
           !normalized.Contains(separator + ".external" + separator, StringComparison.OrdinalIgnoreCase) &&
           !normalized.Contains(separator + ".git" + separator, StringComparison.OrdinalIgnoreCase);
}

static void AssertDoesNotContainAny(string text, IEnumerable<string> forbiddenTokens, string subject)
{
    foreach (string token in forbiddenTokens)
    {
        if (text.Contains(token, StringComparison.Ordinal))
            throw new InvalidOperationException($"{subject} contains forbidden dependency token {token}.");
    }
}

internal static class TestLog
{
    public static ListLoggerProvider Provider { get; } = new();
}

internal sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<LogRecord> _records = new();

    public ILogger CreateLogger(string categoryName)
    {
        return new ListLogger(categoryName, Add);
    }

    public IReadOnlyList<LogRecord> Records
    {
        get
        {
            lock (_records)
                return _records.ToArray();
        }
    }

    public bool Contains(LogLevel level, string messagePart)
    {
        return Records.Any(record =>
            record.Level == level &&
            record.Message.Contains(messagePart, StringComparison.Ordinal));
    }

    public void Clear()
    {
        lock (_records)
            _records.Clear();
    }

    public void Dispose()
    {
    }

    private void Add(LogRecord record)
    {
        lock (_records)
            _records.Add(record);
    }
}

internal sealed class ListLogger : ILogger
{
    private readonly string _categoryName;
    private readonly Action<LogRecord> _write;

    public ListLogger(string categoryName, Action<LogRecord> write)
    {
        _categoryName = categoryName;
        _write = write;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullDisposable.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _write(new LogRecord(_categoryName, logLevel, formatter(state, exception), exception));
    }
}

internal sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Exception);

internal sealed record TestCdbRow(int Id, int Type, int LevelInfo, int Defense, string Name);

internal sealed class NullDisposable : IDisposable
{
    public static NullDisposable Instance { get; } = new();

    private NullDisposable()
    {
    }

    public void Dispose()
    {
    }
}
