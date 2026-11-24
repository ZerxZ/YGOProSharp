using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Specialized;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Cli;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;
using YGOProSharp.NativeApi;
using YGOProSharp.Server;
using YGOProSharp.Server.Addons;

ListLoggerProvider logProvider = TestLog.Provider;
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddProvider(logProvider);
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
Run("CLI maps server options", CliMapsServerOptions);
Run("CLI maps WindBot options", CliMapsWindBotOptions);
Run("CLI maps embedded server bot options", CliMapsEmbeddedServerBotOptions);
Run("WindBot packet factory writes CTOS message byte", WindBotPacketFactoryWritesCtosMessageByte);
Run("WindBot deck loads through named-card repository", WindBotDeckLoadsThroughNamedCardRepository);
Run("WindBot server mode parses query info", WindBotServerModeParsesQueryInfo);
Run("WindBot server mode rejects malformed query params", WindBotServerModeRejectsMalformedQueryParameters);
Run("WindBot server mode and CLI parse WebSocket options", WindBotServerModeAndCliParseWebSocketOptions);
Run("WindBot decks manager initializes lazily and resolves by name or file", WindBotDecksManagerInitializesLazilyAndResolvesByNameAndFile);
Run("WindBot upstream sync supports altart alias, new decks, and new Lua hooks", WindBotUpstreamSyncSupportsAltartNewDecksAndHooks);
Run("WindBot Lua deck executor binds globals, registers rules, and disposes resources", WindBotLuaDeckExecutorBindsGlobalsRegistersRulesAndDisposes);
Run("WindBot Lua deck executor supports module table hooks, LuaTable list conversion, and fallback", WindBotLuaDeckExecutorSupportsModuleTableHooksAndFallback);
Run("WindBot Lua deck executor isolates or rethrows script errors by option", WindBotLuaDeckExecutorHandlesScriptErrors);
Run("WindBot Lua xmake-style DSL supports scopes, rules, deferred functions, and hooks", WindBotLuaXmakeStyleDslSupportsScopesRulesAndHooks);
Run("CLI NLua runtime executes ExampleOldSchool.lua and Lua 5.4 features end-to-end", CliNLuaRuntimeExecutesExampleScriptAndLua54Features);
Run("WindBot decks manager resolves registered, overriding, and dynamic Lua decks", WindBotDecksManagerResolvesLuaDecksAndFileSystemProvider);
Run("WindBot GameBehavior disposes Lua deck executor when deck file is missing", WindBotGameBehaviorDisposesLuaExecutorOnFailure);
Run("Project boundaries keep concrete Lua libraries out of Abstractions and WindBot", ProjectBoundariesKeepConcreteLuaLibrariesOut);
Run("WindBot dialogs degrades gracefully when dialog file is missing", WindBotDialogsDegradesGracefullyWhenDialogFileIsMissing);
Run("WindBot Lua dialog xmake-style DSL supports scopes, fallback, dynamic hooks, and disposal", WindBotLuaDialogXmakeStyleDslSupportsScopesFallbackAndDynamicHooks);
Run("WindBot Lua dialog supports module table, override priority, error isolation, and GameBehavior disposal", WindBotLuaDialogSupportsModuleTablePriorityAndErrorIsolation);
Run("CLI NLua runtime executes ExampleBlueEyes Lua dialog end-to-end", CliNLuaRuntimeExecutesExampleBlueEyesDialogEndToEnd);
Run("WindBot dialogs supports localization for JSON, split Lua files, and default Decks/Lua & Dialogs/Lua folders", WindBotDialogsSupportsLocalizationAndConsolidatedFolders);
Run("WindBot chat pacer paces bursts, caps queue, and respects duel-start delay", WindBotChatPacerPacesBurstsAndRespectsDelay);
await RunAsync("Server broadcasts STOC_CHAT back to sender and opponent for MDPro3 compatibility", ServerBroadcastsChatToSenderAndOpponentAsync);
Run("WindBot game behavior throws clear error when deck is missing", WindBotGameBehaviorThrowsClearErrorWhenDeckIsMissing);
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
Run("Player parses variable length chat packet", PlayerParsesVariableLengthChatPacket);
Run("GameAnalyser parses SelectChain and SelectEffectYn payloads", GameAnalyserParsesSelectChainAndSelectEffectYnPayloads);
Run("Game logs native errors", GameLogsNativeErrors);
Run("CoreMessageReader skip advances position and checks bounds", CoreMessageReaderSkipAdvancesAndBoundsChecks);
Run("NamedCard repository caches GetAllCards snapshot", NamedCardRepositoryCachesAllCardsSnapshot);
Run("Banlist O(1) lookup preserves duplicate-entry precedence", BanlistQuantityLookupPreservesPrecedence);
Run("PacketFramer compacts in place across multiple rounds", PacketFramerCompactsInPlaceAcrossMultipleRounds);
Run("Binary extensions round-trip stackalloc and pooled buffers", BinaryExtensionsRoundTripsStackAllocAndPooledBuffers);
await RunAsync("NetworkClient loopback send and receive", NetworkClientLoopbackSendAndReceiveAsync);
await RunAsync("YGOClient stackalloc Send writes exact CTOS packets", YgoClientStackAllocSendWritesCtosPacketsAsync);
await RunAsync("YGOClient WebSocket round-trips LengthPrefixed and MessagePerPacket modes", YgoClientWebSocketRoundTripsBothFramingModesAsync);
await RunAsync("WindBot client connects to loopback server and sends deck", WindBotClientConnectsToLoopbackServerAndSendsDeckAsync);
await RunAsync("WindBot client connects over WebSocket and sends deck", WindBotClientConnectsOverWebSocketAndSendsDeckAsync);
await RunAsync("InMemoryNetworkTransport round-trips packets and propagates disconnect", InMemoryNetworkTransportRoundTripsAndPropagatesDisconnectAsync);
await RunAsync("Embedded server host connects local WindBot session and local client in memory", EmbeddedServerHostConnectsLocalWindBotSessionAndClientInMemoryAsync);
Run("WindBotDirectClient produces CTOS packets synchronously from fed STOC packets", WindBotDirectClientProducesCtosPacketsSynchronously);
Run("WindBot bundled Decks and Dialogs assets load from output directory", WindBotBundledDecksAndDialogsAssetsLoadFromOutputDirectory);
await RunAsync("WindBotService StartBot captures startup error without leaking session", WindBotServiceStartBotCapturesStartupErrorAsync);
Run("Core layered DIY resources override cards, scripts, archives, and banlists", CoreLayeredDiyResourcesOverrideCardsScriptsAndBanlists);
Run("RoomPasswordParser parses built-in and custom DIY tokens", RoomPasswordParserParsesBuiltInAndCustomDiyTokens);
await RunAsync("Server extensions support addons, custom deck validator, chat commands, and duel preload", ServerExtensionsSupportAddonsDeckValidatorChatCommandsAndDuelPreloadAsync);
await RunAsync("CoreServer multi-room routes concurrent rooms and cleans up closed rooms", CoreServerMultiRoomRoutesConcurrentRoomsAndCleansUpAsync);
Run("Server auto-discovers expansions/diy assets and CLI maps DIY & multi-room options", ServerAutoDiscoversExpansionsAndCliMapsOptions);

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
    string windBotProject = Path.Combine(root, "YGOProSharp.WindBot");
    string cliProject = Path.Combine(root, "YGOProSharp.Cli");

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

    AssertDoesNotContainAny(
        ProjectText(serverProject),
        ["Config.Load", "RunAsync(string[] args", "CliConfiguration", "CliOptionsFactory"],
        "Server library entry boundary");

    string nonCliEntryText = string.Join(Environment.NewLine, [
        ProjectText(coreProject),
        ProjectText(protocolProject),
        ProjectText(serverProject),
        ProjectText(windBotProject)
    ]);
    AssertDoesNotContainAny(
        nonCliEntryText,
        ["Console.CancelKeyPress", "AddSimpleConsole", "CliConfiguration", "CliOptionsFactory"],
        "Non-CLI entry boundary");

    string cliText = ProjectText(cliProject);
    foreach (string requiredCliToken in new[] { "Console.CancelKeyPress", "AddSimpleConsole", "CliConfiguration", "CliOptionsFactory" })
    {
        if (!cliText.Contains(requiredCliToken, StringComparison.Ordinal))
            throw new InvalidOperationException($"CLI project should own {requiredCliToken}.");
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
    AssertDoesNotContainAny(
        projectFile,
        ["OutputType", "StartupObject", "Microsoft.Extensions.Logging.Console"],
        "WindBot library project boundary");

    AssertDoesNotContainAny(
        sourceText,
        ["static void Main", "Environment.Exit", "Config.Load"],
        "WindBot library entry boundary");

    foreach (string requiredReference in new[] { "YGOProSharp.Abstractions", "YGOProSharp.Core", "YGOProSharp.Protocol" })
    {
        if (!projectFile.Contains(requiredReference, StringComparison.Ordinal))
            throw new InvalidOperationException($"WindBot project must reference {requiredReference}.");
    }
}

static void CliMapsServerOptions()
{
    CliConfiguration configuration = CliConfiguration.Load([
        "RootPath=.",
        "ScriptDirectory=scripts",
        "DatabaseFile=cards-ja.cdb",
        "BanlistFile=custom-lflist.conf",
        "Port=7922",
        "ClientVersion=0x1362",
        "StandardStreamProtocol=true",
        "Rule=1",
        "MasterRule=5",
        "Mode=2",
        "StartLp=4000",
        "StartHand=4",
        "DrawCount=2",
        "GameTimer=120",
        "NoCheckDeck=true",
        "NoShuffleDeck=true",
        "MainDeckMinSize=1",
        "MainDeckMaxSize=80",
        "ExtraDeckMaxSize=20",
        "SideDeckMaxSize=20"
    ]);

    ServerOptions options = CliOptionsFactory.CreateServerOptions(configuration);

    AssertEqual(".", options.RootPath);
    AssertEqual("scripts", options.ScriptDirectory);
    AssertEqual("cards-ja.cdb", options.DatabaseFile);
    AssertEqual("custom-lflist.conf", options.BanlistFile);
    AssertEqual(7922, options.Port);
    AssertEqual(0x1362U, options.ClientVersion);
    AssertTrue(options.StandardStreamProtocol);
    AssertEqual(1, options.Game.Region);
    AssertEqual(5, options.Game.MasterRule);
    AssertEqual(2, options.Game.Mode);
    AssertEqual(4000, options.Game.StartLp);
    AssertEqual(4, options.Game.StartHand);
    AssertEqual(2, options.Game.DrawCount);
    AssertEqual(120, options.Game.GameTimer);
    AssertTrue(options.Game.NoCheckDeck);
    AssertTrue(options.Game.NoShuffleDeck);
    AssertEqual(1, options.Game.DeckRules.MainDeckMinSize);
    AssertEqual(80, options.Game.DeckRules.MainDeckMaxSize);
    AssertEqual(20, options.Game.DeckRules.ExtraDeckMaxSize);
    AssertEqual(20, options.Game.DeckRules.SideDeckMaxSize);
}

static void CliMapsEmbeddedServerBotOptions()
{
    CliConfiguration defaultConfig = CliConfiguration.Load(["Port=7911"]);
    AssertFalse(CliOptionsFactory.ShouldStartServerBot(defaultConfig));

    CliConfiguration botDeckConfig = CliConfiguration.Load([
        "Port=7922",
        "ClientVersion=0x1362",
        "BotName=LuaBot",
        "BotDeck=ExampleOldSchool",
        "BotChat=false",
        "BotDebug=true"
    ]);

    ServerOptions serverOptions = CliOptionsFactory.CreateServerOptions(botDeckConfig);
    AssertTrue(CliOptionsFactory.ShouldStartServerBot(botDeckConfig));

    WindBot.WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(botDeckConfig, serverOptions);
    AssertEqual("LuaBot", botInfo.Name);
    AssertEqual("ExampleOldSchool", botInfo.Deck);
    AssertEqual(7922, botInfo.Port);
    AssertEqual(0x1362, botInfo.Version);
    AssertFalse(botInfo.Chat);
    AssertTrue(botInfo.Debug);
}

static void CliMapsWindBotOptions()
{
    INamedCardRepository repository = TestNamedRepository(TestNamedCard(1));
    CliConfiguration configuration = CliConfiguration.Load([
        "Name=Bot",
        "Deck=AI",
        "DeckFile=Smoke.ydk",
        "Dialog=default",
        "Host=127.0.0.1",
        "Port=7911",
        "HostInfo=abc",
        "Version=4962",
        "Hand=1",
        "Debug=true",
        "Chat=false",
        "TickDelayMilliseconds=5",
        "ServerPort=2400",
        "BotListPath=bots.json"
    ]);

    WindBot.WindBotInfo info = CliOptionsFactory.CreateWindBotInfo(configuration);
    WindBot.WindBotRuntimeOptions runtimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(configuration, repository);
    WindBot.WindBotServerModeOptions serverModeOptions = CliOptionsFactory.CreateWindBotServerModeOptions(configuration, runtimeOptions);

    AssertEqual("Bot", info.Name);
    AssertEqual("AI", info.Deck);
    AssertEqual("Smoke.ydk", info.DeckFile);
    AssertEqual("default", info.Dialog);
    AssertEqual("127.0.0.1", info.Host);
    AssertEqual(7911, info.Port);
    AssertEqual("abc", info.HostInfo);
    AssertEqual(4962, info.Version);
    AssertEqual(1, info.Hand);
    AssertTrue(info.Debug);
    AssertFalse(info.Chat);
    AssertEqual(5, runtimeOptions.TickDelayMilliseconds);
    AssertEqual(2400, serverModeOptions.ServerPort);
    AssertEqual("bots.json", serverModeOptions.BotListPath);
    if (!ReferenceEquals(repository, runtimeOptions.CardRepository))
        throw new InvalidOperationException("WindBot runtime options should keep the repository supplied by CLI.");
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

static void WindBotServerModeRejectsMalformedQueryParameters()
{
    AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
        WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=notanumber")) is null);
    AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
        WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=70000")) is null);
    AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
        WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=7911&debug=invalid")) is null);
    AssertTrue(WindBot.WindBotServerModeHost.CreateInfoFromQuery(
        WindBot.QueryStringParser.ParseQueryString("name=Bot&host=127.0.0.1&port=7911&wsframing=invalid")) is null);
}

static void WindBotServerModeAndCliParseWebSocketOptions()
{
    WindBot.WindBotInfo? wsUriInfo = WindBot.WindBotServerModeHost.CreateInfoFromQuery(
        WindBot.QueryStringParser.ParseQueryString("name=WsBot&host=wss://example.com:8443/ygopro&wsframing=MessagePerPacket"));
    if (wsUriInfo is null)
        throw new InvalidOperationException("Expected ws:// or wss:// host without explicit port param to succeed.");

    AssertTrue(wsUriInfo.UseWebSocket);
    AssertEqual(8443, wsUriInfo.Port);
    AssertEqual((int)WebSocketFramingMode.MessagePerPacket, (int)wsUriInfo.WebSocketFramingMode);

    CliConfiguration cliConfig = CliConfiguration.Load([
        "Name=CliWsBot",
        "Host=127.0.0.1",
        "Port=7911",
        "UseWebSocket=true",
        "WebSocketUseWss=true",
        "WebSocketPath=/ygo",
        "WebSocketFramingMode=MessagePerPacket"
    ]);
    WindBot.WindBotInfo cliInfo = CliOptionsFactory.CreateWindBotInfo(cliConfig);
    AssertTrue(cliInfo.UseWebSocket);
    AssertTrue(cliInfo.WebSocketUseWss);
    AssertEqual("/ygo", cliInfo.WebSocketPath);
    AssertEqual((int)WebSocketFramingMode.MessagePerPacket, (int)cliInfo.WebSocketFramingMode);
}

static void WindBotDecksManagerInitializesLazilyAndResolvesByNameAndFile()
{
    WindBot.WindBotInfo byNameInfo = new() { Deck = "Yubel" };
    WindBot.Game.GameClient byNameClient = new(byNameInfo, TestNamedRepository());
    WindBot.Game.Duel duel = new();
    WindBot.Game.GameAI aiByName = new(byNameClient, duel);

    WindBot.Game.AI.Executor execByName = WindBot.Game.AI.DecksManager.Instantiate(aiByName, duel);
    AssertEqual("AI_Yubel", execByName.Deck);

    WindBot.WindBotInfo byFileInfo = new() { Deck = "AI_Yubel" };
    WindBot.Game.GameClient byFileClient = new(byFileInfo, TestNamedRepository());
    WindBot.Game.GameAI aiByFile = new(byFileClient, duel);

    WindBot.Game.AI.Executor execByFile = WindBot.Game.AI.DecksManager.Instantiate(aiByFile, duel);
    AssertEqual("AI_Yubel", execByFile.Deck);
}

static void WindBotUpstreamSyncSupportsAltartNewDecksAndHooks()
{
    // 1. 验证 NamedCard.IsAltartAlias 异画判定（含 5405695 特例）
    AssertTrue(NamedCard.IsAltartAlias(89631140, 89631139));
    AssertFalse(NamedCard.IsAltartAlias(89631160, 89631139));
    AssertFalse(NamedCard.IsAltartAlias(5405695, 5405694));

    // 2. 验证从上游同步的全新卡组执行器均可正常注册与实例化
    WindBot.Game.Duel duel = new();
    foreach (string deckName in new[] { "Elfnote", "ChaosRitual", "RadiantTyphoon", "Voiceless", "SacredBeast", "Monarch506" })
    {
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = deckName }, TestNamedRepository());
        WindBot.Game.GameAI ai = new(client, duel);
        WindBot.Game.AI.Executor exec = WindBot.Game.AI.DecksManager.Instantiate(ai, duel);
        AssertTrue(exec is not null && !string.IsNullOrWhiteSpace(exec.Deck));
    }

    // 3. 验证上游新增的 Executor 钩子已同步接入 LuaDeckExecutor 与 xmake 风格 DSL
    int posChangeCount = 0;
    FakeLuaRuntime runtime = new(context =>
    {
        var onPosChange = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_pos_change"]!;
        var onSelectDisfield = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_disfield"]!;
        var onSelectDirect = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_battle_direct_attack"]!;

        onPosChange(new FakeRawLuaClosure(_ => { posChangeCount++; return null; }));
        onSelectDisfield(new FakeRawLuaClosure(_ => (double)0x04));
        onSelectDirect(new FakeRawLuaClosure(_ => false));
    });

    WindBot.Game.GameClient luaClient = new(new WindBot.WindBotInfo { Name = "CtxBot", Deck = "HookTest" }, TestNamedRepository());
    WindBot.Game.GameAI luaAi = new(luaClient, duel);
    using WindBot.Game.AI.Lua.LuaDeckExecutor luaExec = new(luaAi, duel, runtime, "-- hooks", "HookTest.lua");

    luaExec.OnPosChange(new WindBot.Game.ClientCard(1, CardLocation.MonsterZone, 0), 1, 4);
    AssertEqual(1, posChangeCount);
    AssertEqual(0x04U, luaExec.OnSelectDisfield(0, 1, 0x1FU));
    AssertFalse(luaExec.OnSelectBattleDirectAttack(new WindBot.Game.ClientCard(1, CardLocation.MonsterZone, 0), true));
    AssertTrue(luaClient.GetLogContext().Contains("Bot=CtxBot", StringComparison.Ordinal));
}

static void WindBotDialogsDegradesGracefullyWhenDialogFileIsMissing()
{
    WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
    {
        Dialog = "non_existent_dialog_" + Guid.NewGuid().ToString("N"),
        Chat = true
    }, TestNamedRepository());

    WindBot.Game.AI.Dialogs dialogs = new(client);
    dialogs.SendWelcome();
    dialogs.SendSorry();
    dialogs.SendDeckSorry("DECK");
    dialogs.SendCustomChat(0);
}

static void WindBotGameBehaviorThrowsClearErrorWhenDeckIsMissing()
{
    WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
    {
        Deck = "Yubel",
        DeckFile = "missing_deck_" + Guid.NewGuid().ToString("N")
    }, TestNamedRepository());

    AssertThrows<InvalidOperationException>(() => _ = new WindBot.Game.GameBehavior(client, TestNamedRepository()));
}

static async Task WindBotClientConnectsToLoopbackServerAndSendsDeckAsync()
{
    string previousDirectory = Environment.CurrentDirectory;
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotSmoke_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

    try
    {
        File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
        [
            "1",
            "!side",
            "2"
        ]);

        Environment.CurrentDirectory = temporaryDirectory;

        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        INamedCardRepository repository = TestNamedRepository(
            TestNamedCard(1),
            TestNamedCard(2));

        WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = repository,
            TickDelayMilliseconds = 5
        });

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        Task<Socket> acceptTask = listener.AcceptSocketAsync(cts.Token).AsTask();
        Task botTask = service.RunBotAsync(new WindBot.WindBotInfo
        {
            Name = "SmokeBot",
            Host = "127.0.0.1",
            Port = port,
            Deck = "AI_Yubel",
            Chat = false
        }, cts.Token);

        using Socket serverSocket = await WaitAsync(acceptTask);
        using NetworkClient serverClient = new(serverSocket);
        PacketFramer framer = new();
        List<byte[]> receivedPackets = new();
        TaskCompletionSource<bool> handshakeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> readyReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        serverClient.DataReceived += data =>
        {
            framer.Append(data);
            while (framer.TryReadPacket(out byte[] packet))
            {
                lock (receivedPackets)
                {
                    receivedPackets.Add(packet);
                    if (receivedPackets.Count >= 3)
                        handshakeDone.TrySetResult(true);
                    if (packet.Length > 0 && packet[0] == (byte)CtosMessage.HsReady)
                        readyReceived.TrySetResult(true);
                }
            }
        };
        serverClient.BeginReceive(cts.Token);

        await WaitAsync(handshakeDone.Task);

        // 发送 JoinGame 和 TypeChange 后，确认 WindBot 会提交卡组并准备就绪。
        using (MemoryStream joinPayload = new())
        using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)StocMessage.JoinGame);
            writer.Write(0U);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)5);
            await serverClient.SendAsync(framer.Frame(joinPayload.ToArray()), cts.Token);
        }

        await serverClient.SendAsync(framer.Frame([(byte)StocMessage.TypeChange, (byte)PlayerType.Host]), cts.Token);
        await WaitAsync(readyReceived.Task);

        lock (receivedPackets)
        {
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.PlayerInfo));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.JoinGame));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.UpdateDeck));
            AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady));
        }

        cts.Cancel();
        await botTask;
        serverClient.Close();
        listener.Stop();
    }
    finally
    {
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static int GetAvailableTcpPort()
{
    using TcpListener probe = new(IPAddress.Loopback, 0);
    probe.Start();
    return ((IPEndPoint)probe.LocalEndpoint).Port;
}

static async Task YgoClientWebSocketRoundTripsBothFramingModesAsync()
{
    await VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode.LengthPrefixed);
    await VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode.MessagePerPacket);
}

static async Task VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode mode)
{
    int port = GetAvailableTcpPort();
    using HttpListener listener = new();
    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();

    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
    Task<HttpListenerContext> contextTask = listener.GetContextAsync();

    YGOClient client = YGOClient.CreateWebSocket(mode);
    List<byte[]> clientReceived = new();
    TaskCompletionSource<bool> clientReceivedTwo = new(TaskCreationOptions.RunContinuationsAsynchronously);
    client.PacketReceivedRaw += payload =>
    {
        clientReceived.Add(payload.ToArray());
        if (clientReceived.Count >= 2)
            clientReceivedTwo.TrySetResult(true);
    };

    Uri wsUri = new($"ws://127.0.0.1:{port}/ws");
    Task connectTask = client.ConnectAsync(wsUri, cts.Token);

    HttpListenerContext httpContext = await WaitAsync(contextTask);
    HttpListenerWebSocketContext wsContext = await WaitAsync(httpContext.AcceptWebSocketAsync(subProtocol: null));
    using WebSocket serverWs = wsContext.WebSocket;
    await connectTask;

    // 客户端发两个 CTOS 包
    client.Send(CtosMessage.HsReady);
    client.Send(CtosMessage.HandResult, 3);

    PacketFramer serverFramer = new();
    List<byte[]> serverReceived = new();
    byte[] readBuf = new byte[1024];
    while (serverReceived.Count < 2)
    {
        WebSocketReceiveResult res = await WaitAsync(serverWs.ReceiveAsync(readBuf, cts.Token));
        if (mode == WebSocketFramingMode.LengthPrefixed)
        {
            serverFramer.Append(readBuf.AsSpan(0, res.Count));
            while (serverFramer.TryReadPacket(out byte[] pkt))
                serverReceived.Add(pkt);
        }
        else
        {
            serverReceived.Add(readBuf.AsSpan(0, res.Count).ToArray());
        }
    }

    AssertSequenceEqual(new byte[] { (byte)CtosMessage.HsReady }, serverReceived[0]);
    AssertSequenceEqual(new byte[] { (byte)CtosMessage.HandResult, 3, 0, 0, 0 }, serverReceived[1]);

    // 服务端发回两个 STOC 包（LengthPrefixed 测试粘包发送；MessagePerPacket 测试多分片发送）
    byte[] reply1 = [(byte)StocMessage.SelectHand];
    byte[] reply2 = [(byte)StocMessage.TypeChange, 0x10];
    if (mode == WebSocketFramingMode.LengthPrefixed)
    {
        byte[] sticky = [.. serverFramer.Frame(reply1), .. serverFramer.Frame(reply2)];
        await serverWs.SendAsync(sticky, WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
    }
    else
    {
        await serverWs.SendAsync(reply1, WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
        // 分两片发送 reply2，验证 WebSocketNetworkClient 正确拼包
        await serverWs.SendAsync(reply2.AsMemory(0, 1), WebSocketMessageType.Binary, endOfMessage: false, cts.Token);
        await serverWs.SendAsync(reply2.AsMemory(1, 1), WebSocketMessageType.Binary, endOfMessage: true, cts.Token);
    }

    while (!clientReceivedTwo.Task.IsCompleted && !cts.IsCancellationRequested)
    {
        client.Update();
        await Task.Delay(5, cts.Token);
    }
    await WaitAsync(clientReceivedTwo.Task);

    AssertSequenceEqual(reply1, clientReceived[0]);
    AssertSequenceEqual(reply2, clientReceived[1]);

    client.Close();
    listener.Stop();
}

static async Task WindBotClientConnectsOverWebSocketAndSendsDeckAsync()
{
    string previousDirectory = Environment.CurrentDirectory;
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpWindBotWsSmoke_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

    try
    {
        File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
        [
            "1",
            "!side",
            "2"
        ]);

        Environment.CurrentDirectory = temporaryDirectory;

        int port = GetAvailableTcpPort();
        using HttpListener listener = new();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        INamedCardRepository repository = TestNamedRepository(
            TestNamedCard(1),
            TestNamedCard(2));

        WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = repository,
            TickDelayMilliseconds = 5
        });

        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
        Task<HttpListenerContext> acceptTask = listener.GetContextAsync();
        Task botTask = service.RunBotAsync(new WindBot.WindBotInfo
        {
            Name = "WsSmokeBot",
            Host = $"ws://127.0.0.1:{port}/ygopro",
            Deck = "AI_Yubel",
            Chat = false
        }, cts.Token);

        HttpListenerContext httpContext = await WaitAsync(acceptTask);
        AssertTrue(httpContext.Request.IsWebSocketRequest);
        HttpListenerWebSocketContext wsContext = await WaitAsync(httpContext.AcceptWebSocketAsync(subProtocol: null));
        using WebSocket serverWs = wsContext.WebSocket;

        PacketFramer framer = new();
        List<byte[]> receivedPackets = new();
        byte[] recvBuf = new byte[2048];

        while (receivedPackets.Count < 3)
        {
            WebSocketReceiveResult res = await WaitAsync(serverWs.ReceiveAsync(recvBuf, cts.Token));
            framer.Append(recvBuf.AsSpan(0, res.Count));
            while (framer.TryReadPacket(out byte[] pkt))
                receivedPackets.Add(pkt);
        }

        // 发送 JoinGame 与 TypeChange，确认 WindBot 通过 WebSocket 提交卡组并准备就绪
        using (MemoryStream joinPayload = new())
        using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)StocMessage.JoinGame);
            writer.Write(0U);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)5);
            await serverWs.SendAsync(framer.Frame(joinPayload.ToArray()), WebSocketMessageType.Binary, true, cts.Token);
        }

        await serverWs.SendAsync(framer.Frame([(byte)StocMessage.TypeChange, (byte)PlayerType.Host]), WebSocketMessageType.Binary, true, cts.Token);

        while (!receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady))
        {
            WebSocketReceiveResult res = await WaitAsync(serverWs.ReceiveAsync(recvBuf, cts.Token));
            framer.Append(recvBuf.AsSpan(0, res.Count));
            while (framer.TryReadPacket(out byte[] pkt))
                receivedPackets.Add(pkt);
        }

        AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.PlayerInfo));
        AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.JoinGame));
        AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.UpdateDeck));
        AssertTrue(receivedPackets.Any(p => p.Length > 0 && p[0] == (byte)CtosMessage.HsReady));

        cts.Cancel();
        await botTask;
        listener.Stop();
    }
    finally
    {
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(temporaryDirectory, recursive: true);
    }
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
    using MemoryStream stream = new();
    using BinaryWriter writer = new(stream);
    writer.Write((byte)CtosMessage.PlayerInfo);
    writer.WriteUnicode("Tester", 20);

    Player player = new(new Game(new CoreServer()), new YGOClient());

    player.Parse(stream.ToArray());

    AssertEqual("Tester", player.Name);
    AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Player identified as Tester."));
}

static void PlayerParsesVariableLengthChatPacket()
{
    TestLog.Provider.Clear();
    Game game = new(new CoreServer());
    Player player = new(game, new YGOClient());

    using (MemoryStream infoStream = new())
    using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)CtosMessage.PlayerInfo);
        writer.WriteUnicode("Chatter", 20);
        player.Parse(infoStream.ToArray());
    }

    using (MemoryStream joinStream = new())
    using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)CtosMessage.JoinGame);
        writer.Write((short)0x1362);
        writer.Write(0);
        writer.Write((short)0);
        player.Parse(joinStream.ToArray());
    }

    using MemoryStream chatStream = new();
    using (BinaryWriter writer = new(chatStream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write((byte)CtosMessage.Chat);
        writer.WriteUnicodeAutoLength("你好，决斗者！", 255);
    }

    byte[] chatPacket = chatStream.ToArray();
    PacketReader reader = new(chatPacket.AsSpan(1));
    AssertEqual("你好，决斗者！", reader.ReadUnicodeAutoLength(256));

    player.Parse(chatPacket);
    AssertTrue(TestLog.Provider.Contains(LogLevel.Information, "Player Chatter sent chat message."));
}

static void GameAnalyserParsesSelectChainAndSelectEffectYnPayloads()
{
    Game game = new(new CoreServer());
    GameAnalyser analyser = new(game);
    Player p0 = new(game, new YGOClient());
    Player p1 = new(game, new YGOClient());
    game.CurPlayers[0] = p0;
    game.CurPlayers[1] = p1;

    // SelectEffectYn: player(1) + code(4) + info_location(4) + description(4) = 13 字节
    byte[] effectYnPayload = new byte[13];
    effectYnPayload[0] = 0;
    SequenceReader<byte> effectYnReader = new(new ReadOnlySequence<byte>(effectYnPayload));
    AssertEqual(1, analyser.Analyse(GameMessage.SelectEffectYn, ref effectYnReader, effectYnPayload));
    AssertEqual(0, (int)effectYnReader.Remaining);

    // SelectChain (count=1): player(1) + count(1) + spe_count(1) + hint1(4) + hint2(4) + item(14) = 25 字节
    byte[] selectChainPayload = new byte[25];
    selectChainPayload[0] = 1;
    selectChainPayload[1] = 1;
    SequenceReader<byte> selectChainReader = new(new ReadOnlySequence<byte>(selectChainPayload));
    AssertEqual(1, analyser.Analyse(GameMessage.SelectChain, ref selectChainReader, selectChainPayload));
    AssertEqual(0, (int)selectChainReader.Remaining);

    // ConfirmCards (count=1): player(1) + skip_panel(1) + count(1) + card(code:4 + controller:1 + location:1 + seq:1) = 10 字节
    byte[] confirmCardsPayload = new byte[10];
    confirmCardsPayload[0] = 0;
    confirmCardsPayload[1] = 0;
    confirmCardsPayload[2] = 1;
    confirmCardsPayload[8] = (byte)CardLocation.Hand;
    SequenceReader<byte> confirmCardsReader = new(new ReadOnlySequence<byte>(confirmCardsPayload));
    AssertEqual(0, analyser.Analyse(GameMessage.ConfirmCards, ref confirmCardsReader, confirmCardsPayload));
    AssertEqual(0, (int)confirmCardsReader.Remaining);
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

static void CoreMessageReaderSkipAdvancesAndBoundsChecks()
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

static void NamedCardRepositoryCachesAllCardsSnapshot()
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

static void BanlistQuantityLookupPreservesPrecedence()
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

static void PacketFramerCompactsInPlaceAcrossMultipleRounds()
{
    PacketFramer framer = new();
    byte[] payloadA = Enumerable.Range(0, 128).Select(i => (byte)(i & 0xFF)).ToArray();
    byte[] payloadB = Enumerable.Range(0, 300).Select(i => (byte)((i * 3) & 0xFF)).ToArray();

    for (int round = 0; round < 40; round++)
    {
        byte[] framedA = framer.Frame(payloadA);
        byte[] framedB = framer.Frame(payloadB);

        // 先写入半包，再追加剩余部分和第二个完整粘包，覆盖多次压缩与扩容路径。
        int split = 17;
        framer.Append(framedA.AsSpan(0, split));
        AssertFalse(framer.TryReadPacket(out _));

        byte[] combined = new byte[(framedA.Length - split) + framedB.Length];
        framedA.AsSpan(split).CopyTo(combined);
        framedB.CopyTo(combined.AsSpan(framedA.Length - split));
        framer.Append(combined);

        AssertTrue(framer.TryReadPacket(out byte[]? readA));
        AssertSequenceEqual(payloadA, readA!);
        AssertTrue(framer.TryReadPacket(out byte[]? readB));
        AssertSequenceEqual(payloadB, readB!);
        AssertFalse(framer.TryReadPacket(out _));
    }
}

static void BinaryExtensionsRoundTripsStackAllocAndPooledBuffers()
{
    string shortCjk = "青眼白龙 Blue-Eyes";
    string largeText = new string('龙', 600); // > 1024 bytes in UTF-16 and UTF-8, exercises ArrayPool path

    using MemoryStream stream = new();
    using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.WriteUnicode(string.Empty, 16);
        writer.WriteUnicode(shortCjk, 32);
        writer.WriteUnicode(largeText, 700);
        writer.WriteUnicodeAutoLength(shortCjk, 64);
        writer.WriteUnicodeAutoLength(largeText, 700);
        writer.WriteUtf8(shortCjk, 64);
        writer.WriteUtf8(largeText, 2048);
    }

    stream.Position = 0;
    using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

    AssertEqual(string.Empty, reader.ReadUnicode(16));
    AssertEqual(shortCjk, reader.ReadUnicode(32));
    AssertEqual(largeText, reader.ReadUnicode(700));
    AssertEqual(shortCjk, reader.ReadUnicode(shortCjk.Length + 1));
    AssertEqual(largeText, reader.ReadUnicode(largeText.Length + 1));
    AssertEqual(shortCjk, reader.ReadUtf8(64));
    AssertEqual(largeText, reader.ReadUtf8(2048));
}

static async Task YgoClientStackAllocSendWritesCtosPacketsAsync()
{
    using TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;

    using NetworkClient networkClient = new();
    YGOClient client = new(networkClient);
    Task<Socket> acceptTask = listener.AcceptSocketAsync();

    await networkClient.ConnectAsync(IPAddress.Loopback, port);
    using Socket serverSocket = await acceptTask;

    client.Send(CtosMessage.HsReady);
    client.Send(CtosMessage.Response, 0x12345678);

    byte[] received = new byte[3 + 7];
    int totalRead = 0;
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
    while (totalRead < received.Length)
    {
        int read = await serverSocket.ReceiveAsync(received.AsMemory(totalRead), cts.Token);
        if (read == 0)
            break;
        totalRead += read;
    }

    AssertEqual(received.Length, totalRead);
    AssertSequenceEqual(
        new byte[]
        {
            0x01, 0x00, (byte)CtosMessage.HsReady,
            0x05, 0x00, (byte)CtosMessage.Response, 0x78, 0x56, 0x34, 0x12
        },
        received);

    client.Close();
    listener.Stop();
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
        using StreamWriter err = new(Console.OpenStandardError(), Encoding.UTF8, leaveOpen: true);
        err.WriteLine($"FAIL {name}: {ex}");
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
        using StreamWriter err = new(Console.OpenStandardError(), Encoding.UTF8, leaveOpen: true);
        err.WriteLine($"FAIL {name}: {ex}");
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

static void ProjectBoundariesKeepConcreteLuaLibrariesOut()
{
    string root = FindRepositoryRoot();
    string[] projectFiles =
    [
        Path.Combine(root, "YGOProSharp.Abstractions", "YGOProSharp.Abstractions.csproj"),
        Path.Combine(root, "YGOProSharp.WindBot", "YGOProSharp.WindBot.csproj")
    ];
    string[] forbiddenLuaPackages = ["XLua", "NLua", "MoonSharp", "KeraLua", "LuaCSharp", "Lua.CSharp", "SLua", "ToLua"];

    foreach (string projectFile in projectFiles)
    {
        string xml = File.ReadAllText(projectFile);
        AssertDoesNotContainAny(xml, forbiddenLuaPackages, Path.GetRelativePath(root, projectFile));
    }
}

static void WindBotLuaDeckExecutorBindsGlobalsRegistersRulesAndDisposes()
{
    WindBot.WindBotInfo info = new() { Deck = "TestLua" };
    WindBot.Game.GameClient client = new(info, TestNamedRepository());
    WindBot.Game.Duel duel = new();
    WindBot.Game.GameAI ai = new(client, duel);

    int disposeHookCalls = 0;
    bool customRuleExecuted = false;
    bool rawWrappedRuleExecuted = false;

    FakeLuaRuntime runtime = new(context =>
    {
        context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnInit] = new FakeLuaFunction(args =>
        {
            WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
            exec.AddLuaExecutor("Activate", 1001, "MyCardRule");
            exec.AddLuaExecutor(WindBot.Game.AI.ExecutorType.Summon, 1002, (Func<bool>)(() => true));
            exec.AddLuaExecutor(WindBot.Game.AI.ExecutorType.SpellSet, 1003, new FakeRawLuaClosure(_ =>
            {
                rawWrappedRuleExecuted = true;
                return true;
            }));
            exec.AddDefaultExecutor("Activate", 5318639, "DefaultTrap");
            return null;
        });

        context.Functions["MyCardRule"] = new FakeLuaFunction(args =>
        {
            WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
            customRuleExecuted = exec.CurrentCard != null && exec.CurrentCard.Id == 1001;
            return true;
        });

        context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnDispose] = new FakeLuaFunction(_ =>
        {
            disposeHookCalls++;
            return null;
        });
    });

    WindBot.Game.AI.Lua.LuaDeckExecutor executor = new(ai, duel, runtime, "-- test lua deck", "TestLua.lua");
    FakeLuaContext createdContext = runtime.CreatedContexts[0];

    AssertTrue(ReferenceEquals(executor, createdContext.Globals["Self"]));
    AssertTrue(ReferenceEquals(executor, createdContext.Globals["Executor"]));
    AssertTrue(ReferenceEquals(duel, createdContext.Globals["Duel"]));
    AssertTrue(ReferenceEquals(ai, createdContext.Globals["AI"]));
    AssertTrue(ReferenceEquals(executor.Util, createdContext.Globals["Util"]));
    AssertTrue(ReferenceEquals(duel.Fields[0], createdContext.Globals["Bot"]));
    AssertTrue(ReferenceEquals(duel.Fields[1], createdContext.Globals["Enemy"]));

    WindBot.Game.ClientCard sampleCard = new(1001, CardLocation.Hand, 0);
    executor.SetCard(WindBot.Game.AI.ExecutorType.Activate, sampleCard, 0);
    AssertTrue(executor.Executors.Any(e => e.CardId == 1001 && e.Type == WindBot.Game.AI.ExecutorType.Activate && e.Func()));
    AssertTrue(customRuleExecuted);

    AssertTrue(executor.Executors.Any(e => e.CardId == 1002 && e.Type == WindBot.Game.AI.ExecutorType.Summon && e.Func()));
    AssertTrue(executor.Executors.Any(e => e.CardId == 1003 && e.Type == WindBot.Game.AI.ExecutorType.SpellSet && e.Func()));
    AssertTrue(rawWrappedRuleExecuted);
    AssertTrue(executor.Executors.Any(e => e.CardId == 5318639 && e.Type == WindBot.Game.AI.ExecutorType.Activate));

    executor.Dispose();
    executor.Dispose(); // 验证幂等释放

    AssertTrue(executor.IsDisposed);
    AssertEqual(1, disposeHookCalls);
    AssertTrue(createdContext.Disposed);
    AssertTrue(createdContext.TrackedFunctions.All(f => f.Disposed));
}

static void WindBotLuaDeckExecutorSupportsModuleTableHooksAndFallback()
{
    WindBot.WindBotInfo info = new() { Deck = "ModuleLua" };
    WindBot.Game.GameClient client = new(info, TestNamedRepository());
    WindBot.Game.Duel duel = new();
    WindBot.Game.GameAI ai = new(client, duel);

    int newTurnCalls = 0;
    FakeLuaTable? createdModule = null;

    FakeLuaRuntime runtime = new(context =>
    {
        FakeLuaTable module = new();
        createdModule = module;

        // Lua number 常见为 double，验证安全转 int 与枚举
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnRockPaperScissors] = new FakeLuaFunction(_ => 2.0d);
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectHand] = new FakeLuaFunction(_ => false);
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectOption] = new FakeLuaFunction(_ => 1L);
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectPosition] = new FakeLuaFunction(_ => (double)CardPosition.FaceUpDefence);
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectYesNo] = new FakeLuaFunction(args =>
        {
            int desc = (int)args[1]!;
            return desc == 42 ? false : null; // 返回 null 时应回退基类 DefaultExecutor（返回 true）
        });
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnNewTurn] = new FakeLuaFunction(_ =>
        {
            newTurnCalls++;
            return null;
        });
        // 模拟 XLua 返回自定义 LuaTable 列表对象，经由 ILuaScriptContext.TryConvertList<ClientCard> 转换
        module.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectCard] = new FakeLuaFunction(args =>
        {
            IList<WindBot.Game.ClientCard> cards = (IList<WindBot.Game.ClientCard>)args[1]!;
            return new FakeLuaCardArray([cards[^1]]);
        });

        context.ModuleResult = module;
    });

    using WindBot.Game.AI.Lua.LuaDeckExecutor executor = new(ai, duel, runtime, "return M", "ModuleLua.lua");

    AssertEqual(2, executor.OnRockPaperScissors());
    AssertFalse(executor.OnSelectHand());
    AssertEqual(1, executor.OnSelectOption([10, 20]));
    AssertEqual((int)CardPosition.FaceUpDefence, (int)executor.OnSelectPosition(1, [CardPosition.FaceUpAttack, CardPosition.FaceUpDefence]));
    AssertFalse(executor.OnSelectYesNo(42));
    AssertTrue(executor.OnSelectYesNo(99)); // 回退到 Executor.OnSelectYesNo 默认 true

    executor.OnNewTurn();
    AssertEqual(1, newTurnCalls);

    WindBot.Game.ClientCard c1 = new(101, CardLocation.Hand, 0);
    WindBot.Game.ClientCard c2 = new(102, CardLocation.Hand, 1);
    IList<WindBot.Game.ClientCard> picked = executor.OnSelectCard([c1, c2], 1, 1, 0, false);
    AssertEqual(1, picked.Count);
    AssertEqual(102, picked[0].Id);

    executor.Dispose();
    AssertTrue(createdModule is not null && createdModule.Disposed);
}

static void WindBotLuaDeckExecutorHandlesScriptErrors()
{
    WindBot.WindBotInfo info = new() { Deck = "ErrorLua" };
    WindBot.Game.GameClient client = new(info, TestNamedRepository());
    WindBot.Game.Duel duel = new();
    WindBot.Game.GameAI ai = new(client, duel);

    FakeLuaRuntime runtime = new(context =>
    {
        context.Functions[WindBot.Game.AI.Lua.LuaDeckHookNames.OnSelectYesNo] = new FakeLuaFunction(_ =>
            throw new InvalidOperationException("boom in lua"));
    });

    using WindBot.Game.AI.Lua.LuaDeckExecutor resilient = new(ai, duel, runtime, "-- err", "Err.lua", throwOnScriptError: false);
    AssertTrue(resilient.OnSelectYesNo(1)); // 记录错误后回退默认实现

    using WindBot.Game.AI.Lua.LuaDeckExecutor strict = new(ai, duel, runtime, "-- err", "ErrStrict.lua", throwOnScriptError: true);
    AssertThrows<InvalidOperationException>(() => _ = strict.OnSelectYesNo(1));
}

static void WindBotLuaXmakeStyleDslSupportsScopesRulesAndHooks()
{
    WindBot.Game.Duel duel = new();
    WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = "DeckBeta" }, TestNamedRepository());
    WindBot.Game.GameAI ai = new(client, duel);

    int initCalls = 0;
    int newTurnCalls = 0;
    int disposeCalls = 0;
    bool betaDeferredRuleCalled = false;

    FakeLuaRuntime runtime = new(context =>
    {
        // 模拟类似 xmake.lua 的声明式脚本：
        // 顶层公共规则 + deck("DeckAlpha") ... deck_end() + deck("DeckBeta") ... deck_end()
        var deckFn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["deck"]!;
        var deckEndFn = (WindBot.Game.AI.Lua.LuaDslAction0)context.Globals["deck_end"]!;
        var setDeckFile = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_deckfile"]!;
        var setLevel = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_level"]!;
        var setDescription = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_description"]!;
        var setAuthor = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_author"]!;
        var addDefault = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_default"]!;
        var addExecutors = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_executors"]!;
        var onActivate = (WindBot.Game.AI.Lua.LuaDslAction2)context.Globals["on_activate"]!;
        var onSummon = (WindBot.Game.AI.Lua.LuaDslAction2)context.Globals["on_summon"]!;
        var onInit = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_init"]!;
        var onNewTurn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_new_turn"]!;
        var onSelectYesNo = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_select_yesno"]!;
        var onDispose = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_dispose"]!;

        // 顶层全局规则（对所有 deck 生效，仿照 xmake 顶层作用域）
        addDefault("Repos", "DefaultMonsterRepos", null);

        deckFn("DeckAlpha");
            setDeckFile("AI_Alpha");
            onActivate(1111, new FakeRawLuaClosure(_ => true));
        deckEndFn();

        deckFn("DeckBeta");
            setDeckFile("AI_Beta_Custom");
            setLevel("Hard");
            setDescription("xmake-style Lua deck");
            setAuthor("tester");
            // 先声明规则，引用脚本底部才定义的函数名
            onSummon(2222, "can_summon_beta");
            addExecutors("SummonOrSet", new object[] { 3333.0, 4444L }, null);
            onInit(new FakeRawLuaClosure(_ => { initCalls++; return null; }));
            onNewTurn((Action<WindBot.Game.AI.Lua.LuaDeckExecutor>)(_ => newTurnCalls++));
            onSelectYesNo(new FakeRawLuaClosure(args => (int)args[1]! == 7 ? false : null));
            onDispose(new FakeRawLuaClosure(_ => { disposeCalls++; return null; }));
        deckEndFn();

        // 在脚本尾部才定义函数，验证延迟解析
        context.Functions["can_summon_beta"] = new FakeLuaFunction(args =>
        {
            WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)args[0]!;
            betaDeferredRuleCalled = exec.CurrentCard != null && exec.CurrentCard.Id == 2222;
            return true;
        });
    });

    WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider provider = new();
    provider.Add("DeckBeta", "-- xmake style multi-deck");

    try
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DecksManager.ConfigureLua(runtime, provider);

        WindBot.Game.AI.Lua.LuaDeckExecutor exec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(ai, duel);

        // 验证仅激活 DeckBeta，且 DecksManager 尊重脚本内的 set_deckfile("AI_Beta_Custom")
        AssertEqual("AI_Beta_Custom", exec.Deck);
        AssertEqual("DeckBeta", exec.Dsl.DeclaredDeckName);
        AssertEqual("Hard", exec.Dsl.DeclaredLevel);
        AssertEqual("xmake-style Lua deck", exec.Dsl.DeclaredDescription);
        AssertEqual("tester", exec.Dsl.DeclaredAuthor);
        AssertEqual(1, initCalls);

        // DeckAlpha 的 1111 规则不应生效，顶层 Repos + DeckBeta 的 2222/3333/4444 应按顺序生效
        AssertFalse(exec.Executors.Any(e => e.CardId == 1111));
        int reposIndex = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.Repos && e.CardId == -1);
        int summon2222Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.Summon && e.CardId == 2222);
        int sos3333Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet && e.CardId == 3333);
        int sos4444Index = exec.Executors.ToList().FindIndex(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet && e.CardId == 4444);
        AssertTrue(reposIndex >= 0 && summon2222Index > reposIndex && sos3333Index > summon2222Index && sos4444Index > sos3333Index);

        exec.SetCard(WindBot.Game.AI.ExecutorType.Summon, new WindBot.Game.ClientCard(2222, CardLocation.Hand, 0), 0);
        AssertTrue(exec.Executors[summon2222Index].Func());
        AssertTrue(betaDeferredRuleCalled);

        exec.OnNewTurn();
        AssertEqual(1, newTurnCalls);
        AssertFalse(exec.OnSelectYesNo(7));
        AssertTrue(exec.OnSelectYesNo(8)); // nil 回退默认 true

        exec.Dispose();
        AssertEqual(1, disposeCalls);
        AssertTrue(runtime.CreatedContexts[0].Disposed);
        AssertTrue(runtime.CreatedContexts[0].TrackedFunctions.All(f => f.Disposed));
    }
    finally
    {
        WindBot.Game.AI.DecksManager.ResetLua();
    }
}

static void CliNLuaRuntimeExecutesExampleScriptAndLua54Features()
{
    string root = FindRepositoryRoot();
    string luaDecksDir = Path.Combine(root, "YGOProSharp.WindBot", "Decks", "Lua");

    // 1. 验证 CLI 默认自动装配 NLuaRuntime 并解析自定义 LuaDeckDir 与开关
    INamedCardRepository cardRepo = TestNamedRepository(
        TestNamedCard(1000, attack: 1000, defense: 1000),
        TestNamedCard(1001, attack: 500, defense: 500),
        TestNamedCard(1002, attack: 2500, defense: 2000));

    CliConfiguration cliConfig = CliConfiguration.Load([
        $"LuaDeckDir={luaDecksDir}",
        "PreferLuaDecks=true",
        "ThrowOnLuaScriptError=true"
    ]);
    WindBot.WindBotRuntimeOptions cliRuntimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(cliConfig, cardRepo);
    AssertTrue(cliRuntimeOptions.LuaRuntime is NLuaRuntime);
    AssertTrue(cliRuntimeOptions.LuaScriptProvider is WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider);
    AssertTrue(cliRuntimeOptions.PreferLuaDecks);
    AssertTrue(cliRuntimeOptions.ThrowOnLuaScriptError);

    CliConfiguration disabledConfig = CliConfiguration.Load(["EnableLuaDecks=false"]);
    WindBot.WindBotRuntimeOptions disabledOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(disabledConfig, cardRepo);
    AssertTrue(disabledOptions.LuaRuntime is null);

    // 2. 使用真实 NLua 5.4 运行时加载并执行仓库自带的 ExampleOldSchool.lua
    WindBot.Game.Duel duel = new();
    duel.Fields[0].Init(40, 15, 0);
    duel.Fields[1].Init(40, 15, 1);
    WindBot.Game.GameClient client = new(new WindBot.WindBotInfo { Deck = "ExampleOldSchool" }, cardRepo);
    WindBot.Game.GameAI ai = new(client, duel);

    try
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.WindBotRuntime.Configure(cliRuntimeOptions);

        using WindBot.Game.AI.Lua.LuaDeckExecutor exec =
            (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(ai, duel);

        AssertEqual("AI_OldSchool", exec.Deck);
        AssertEqual("ExampleOldSchool", exec.Dsl.DeclaredDeckName);
        AssertEqual("Easy", exec.Dsl.DeclaredLevel);
        AssertEqual("YGOProSharp", exec.Dsl.DeclaredAuthor);

        // 验证 add_executors 批量注册了 6 只怪兽
        AssertEqual(6, exec.Executors.Count(e => e.Type == WindBot.Game.AI.ExecutorType.SummonOrSet));

        // 触发自定义 Lua 规则 should_activate_swords（敌方存在里侧怪兽 -> true）
        WindBot.Game.AI.CardExecutor swordsExec = exec.Executors.First(e =>
            e.Type == WindBot.Game.AI.ExecutorType.Activate && e.CardId == 72302403);
        WindBot.Game.ClientCard facedownEnemy = new(1000, CardLocation.MonsterZone, 0, (int)CardPosition.FaceDownDefence);
        duel.Fields[1].MonsterZone[0] = facedownEnemy;
        AssertTrue(swordsExec.Func());

        // 敌方仅剩低攻表侧怪兽且己方有高攻怪兽 -> false
        WindBot.Game.ClientCard weakEnemy = new(1001, CardLocation.MonsterZone, 0, (int)CardPosition.FaceUpAttack);
        duel.Fields[1].MonsterZone[0] = weakEnemy;
        WindBot.Game.ClientCard strongBot = new(1002, CardLocation.MonsterZone, 0, (int)CardPosition.FaceUpAttack);
        duel.Fields[0].MonsterZone[0] = strongBot;
        AssertFalse(swordsExec.Func());

        // 3. 验证真实 NLua 5.4 位运算与 Lua table 返回卡牌列表（TryConvertList<ClientCard>）
        const string lua54Script = """
            deck("Lua54FeatureCheck")
                on_select_disfield(function(ctx, hint, count, available)
                    return (1 << 2) & available
                end)
                on_select_card(function(ctx, cards, min, max, hint, cancelable)
                    return { cards[1], cards[0] }
                end)
                on_select_position(function(ctx, cardId, positions)
                    return CardPosition.FaceUpDefence
                end)
            deck_end()
            """;

        using WindBot.Game.AI.Lua.LuaDeckExecutor featureExec = new(
            ai,
            duel,
            NLuaRuntime.Instance,
            lua54Script,
            "Lua54FeatureCheck.lua",
            throwOnScriptError: true,
            targetDeckName: "Lua54FeatureCheck");

        AssertEqual(0x04U, featureExec.OnSelectDisfield(0, 1, 0x1FU));
        AssertEqual((int)CardPosition.FaceUpDefence, (int)featureExec.OnSelectPosition(1, [CardPosition.FaceUpAttack, CardPosition.FaceUpDefence]));

        WindBot.Game.ClientCard first = new(2001, CardLocation.Hand, 0);
        WindBot.Game.ClientCard second = new(2002, CardLocation.Hand, 1);
        IList<WindBot.Game.ClientCard> selected = featureExec.OnSelectCard([first, second], 2, 2, 0, false);
        AssertEqual(2, selected.Count);
        AssertEqual(2002, selected[0].Id);
        AssertEqual(2001, selected[1].Id);

        // 4. 验证仓库自带的 ExampleBlueEyes.lua 可被真实 NLua 5.4 加载并执行青眼规则
        WindBot.Game.GameClient blueEyesClient = new(new WindBot.WindBotInfo { Deck = "ExampleBlueEyes", Chat = false }, cardRepo);
        WindBot.Game.GameAI blueEyesAi = new(blueEyesClient, duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor blueEyesExec =
            (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(blueEyesAi, duel);
        AssertEqual("AI_BlueEyes", blueEyesExec.Deck);
        AssertEqual("ExampleBlueEyes", blueEyesExec.Dsl.DeclaredDeckName);
        AssertTrue(blueEyesExec.Executors.Any(e => e.CardId == 38517737 && e.Type == WindBot.Game.AI.ExecutorType.SpSummon));
        AssertTrue(blueEyesExec.Executors.Any(e => e.CardId == 41620959 && e.Type == WindBot.Game.AI.ExecutorType.Activate));
    }
    finally
    {
        WindBot.Game.AI.DecksManager.ResetLua();
    }
}

static void WindBotDecksManagerResolvesLuaDecksAndFileSystemProvider()
{
    string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDeckTest_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);

    try
    {
        File.WriteAllText(Path.Combine(tempDir, "DynamicBlueEyes.lua"), "-- dynamic");
        File.WriteAllText(Path.Combine(tempDir, "RegisteredHero.lua"), "-- registered");
        File.WriteAllText(Path.Combine(tempDir, "Yubel.lua"), "-- override");

        WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider fsProvider = new(tempDir);
        IReadOnlyList<WindBot.Game.AI.Lua.LuaDeckDescriptor> discovered = fsProvider.DiscoverDecks();
        AssertEqual(3, discovered.Count);

        FakeLuaRuntime runtime = new(_ => { });
        WindBot.Game.Duel duel = new();

        // 1. 注册自定义 Lua 卡组（含自定义 .ydk 文件名）+ 动态按名发现
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DecksManager.ConfigureLua(runtime, fsProvider, preferLuaDecks: false);
        WindBot.Game.AI.DecksManager.RegisterLuaDeck(new WindBot.Game.AI.Lua.LuaDeckDescriptor(
            name: "HeroLua",
            file: "AI_HeroLua",
            scriptName: "RegisteredHero"));

        WindBot.Game.GameAI heroAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "HeroLua" }, TestNamedRepository()), duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor heroExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(heroAi, duel);
        AssertEqual("AI_HeroLua", heroExec.Deck);

        WindBot.Game.GameAI dynamicAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "DynamicBlueEyes" }, TestNamedRepository()), duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor dynamicExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(dynamicAi, duel);
        AssertEqual("DynamicBlueEyes", dynamicExec.Deck);

        // 2. 默认不覆盖内置 C# Yubel；设置 AllowOverride=true 后可覆盖内置 C# 卡组
        WindBot.Game.GameAI yubelManagedAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "Yubel" }, TestNamedRepository()), duel);
        WindBot.Game.AI.Executor yubelManaged = WindBot.Game.AI.DecksManager.Instantiate(yubelManagedAi, duel);
        AssertFalse(yubelManaged is WindBot.Game.AI.Lua.LuaDeckExecutor);

        WindBot.Game.AI.DecksManager.RegisterLuaDeck(new WindBot.Game.AI.Lua.LuaDeckDescriptor(
            name: "Yubel",
            file: "AI_Yubel_Lua",
            scriptName: "Yubel",
            allowOverride: true));
        WindBot.Game.GameAI yubelOverrideAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "Yubel" }, TestNamedRepository()), duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor yubelLua = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(yubelOverrideAi, duel);
        AssertEqual("AI_Yubel_Lua", yubelLua.Deck);

        // 3. 验证通过 WindBotRuntime.Configure 注入 LuaRuntime + InMemoryLuaDeckScriptProvider
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider memoryProvider = new();
        memoryProvider.Add("MemoryDeck", "-- memory lua");
        WindBot.WindBotRuntime.Configure(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = TestNamedRepository(),
            LuaRuntime = runtime,
            LuaScriptProvider = memoryProvider,
            LuaDecks = [new WindBot.Game.AI.Lua.LuaDeckDescriptor("MemoryDeck", "AI_MemoryDeck")]
        });

        WindBot.Game.GameAI memAi = new(new WindBot.Game.GameClient(new WindBot.WindBotInfo { Deck = "MemoryDeck" }, TestNamedRepository()), duel);
        using WindBot.Game.AI.Lua.LuaDeckExecutor memExec = (WindBot.Game.AI.Lua.LuaDeckExecutor)WindBot.Game.AI.DecksManager.Instantiate(memAi, duel);
        AssertEqual("AI_MemoryDeck", memExec.Deck);
    }
    finally
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        Directory.Delete(tempDir, recursive: true);
    }
}

static void WindBotGameBehaviorDisposesLuaExecutorOnFailure()
{
    FakeLuaRuntime runtime = new(_ => { });
    WindBot.Game.AI.Lua.InMemoryLuaDeckScriptProvider memoryProvider = new();
    memoryProvider.Add("LeakCheckDeck", "-- leak check");

    try
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DecksManager.ConfigureLua(runtime, memoryProvider);

        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
        {
            Deck = "LeakCheckDeck",
            DeckFile = "missing_ydk_" + Guid.NewGuid().ToString("N")
        }, TestNamedRepository());

        AssertThrows<InvalidOperationException>(() => _ = new WindBot.Game.GameBehavior(client, TestNamedRepository()));
        AssertEqual(1, runtime.CreatedContexts.Count);
        AssertTrue(runtime.CreatedContexts[0].Disposed);
    }
    finally
    {
        WindBot.Game.AI.DecksManager.ResetLua();
    }
}

static async Task InMemoryNetworkTransportRoundTripsAndPropagatesDisconnectAsync()
{
    // 1. 验证 connected:false -> Connect() 触发两端 Connected 事件与缓冲派发
    (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
        InMemoryNetworkTransport.CreatePair(connected: false);

    bool clientConnected = false;
    bool serverConnected = false;
    bool serverDisconnected = false;

    YGOClient client = new(clientTransport);
    YGOClient server = new(serverTransport);
    client.Connected += () => clientConnected = true;
    server.Connected += () => serverConnected = true;
    server.Disconnected += _ => serverDisconnected = true;

    List<byte[]> serverReceived = new();
    List<byte[]> clientReceived = new();
    server.PacketReceivedRaw += p => serverReceived.Add(p.ToArray());
    client.PacketReceivedRaw += p => clientReceived.Add(p.ToArray());

    clientTransport.Connect();
    client.Update();
    server.Update();

    AssertTrue(clientConnected);
    AssertTrue(serverConnected);

    client.Send(CtosMessage.HsReady);
    await client.SendAsync(new byte[] { (byte)CtosMessage.HandResult, 2, 0, 0, 0 });
    server.Update();

    AssertEqual(2, serverReceived.Count);
    AssertSequenceEqual(new byte[] { (byte)CtosMessage.HsReady }, serverReceived[0]);
    AssertSequenceEqual(new byte[] { (byte)CtosMessage.HandResult, 2, 0, 0, 0 }, serverReceived[1]);

    server.Send(new byte[] { (byte)StocMessage.SelectHand });
    client.Update();

    AssertEqual(1, clientReceived.Count);
    AssertSequenceEqual(new byte[] { (byte)StocMessage.SelectHand }, clientReceived[0]);

    // 2. 验证一端 Close 后对端同步断开
    client.Close();
    server.Update();
    AssertTrue(serverDisconnected);
    AssertFalse(server.IsConnected);

    // 3. 验证 YGOClient.CreateInMemoryPair 便捷工厂
    (YGOClient pairClient, YGOClient pairServer) = YGOClient.CreateInMemoryPair();
    byte[]? receivedFromPair = null;
    pairServer.PacketReceivedRaw += p => receivedFromPair = p.ToArray();
    pairClient.Send(CtosMessage.Surrender);
    pairServer.Update();
    AssertTrue(receivedFromPair is not null);
    AssertSequenceEqual(new byte[] { (byte)CtosMessage.Surrender }, receivedFromPair!);
    pairClient.Close();
}

static async Task EmbeddedServerHostConnectsLocalWindBotSessionAndClientInMemoryAsync()
{
    string previousDirectory = Environment.CurrentDirectory;
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpInMemDuel_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

    try
    {
        File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
        [
            "1",
            "!side",
            "2"
        ]);
        Environment.CurrentDirectory = temporaryDirectory;

        ICardRepository serverCards = TestRepository(TestCard(1), TestCard(2));
        INamedCardRepository botCards = TestNamedRepository(TestNamedCard(1), TestNamedCard(2));

        ServerOptions serverOptions = new()
        {
            EnableNetworkListener = false,
            ClientVersion = 0x1362,
            Game = new GameOptions
            {
                MasterRule = 5,
                NoCheckDeck = true
            }
        };

        await using YGOProSharpServerHost serverHost = new(duelFactory: null, cardRepository: serverCards, options: serverOptions);
        serverHost.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

        AssertTrue(serverHost.IsRunning);
        AssertFalse(serverHost.IsListening);

        // 先接入一个纯内存本地 C# 客户端，观察房间状态与聊天广播
        YGOClient observerClient = serverHost.ConnectLocalClient();
        List<byte[]> observerPackets = new();
        observerClient.PacketReceivedRaw += p => observerPackets.Add(p.ToArray());

        using (MemoryStream infoStream = new())
        using (BinaryWriter writer = new(infoStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.PlayerInfo);
            writer.WriteUnicode("LocalHost", 20);
            observerClient.Send(infoStream.ToArray());
        }
        using (MemoryStream joinStream = new())
        using (BinaryWriter writer = new(joinStream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)CtosMessage.JoinGame);
            writer.Write((short)0x1362);
            writer.Write(0);
            writer.Write((short)0);
            writer.WriteUnicode("", 20);
            observerClient.Send(joinStream.ToArray());
        }

        // 启动进程内托管 WindBot 会话并直接连入嵌入式服务端
        await using WindBot.WindBotService windBotService = new(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = botCards,
            TickDelayMilliseconds = 5
        });

        AssertTrue(windBotService.GetAvailableDecks().Count > 0);

        bool sessionStarted = false;
        bool sessionStopped = false;

        using WindBot.WindBotSession botSession = windBotService.StartBot(
            new WindBot.WindBotInfo
            {
                Name = "InMemBot",
                Deck = "AI_Yubel",
                Chat = false,
                Version = 0x1362
            },
            serverHost.CreateLocalTransport());

        botSession.Started += _ => sessionStarted = true;
        botSession.Stopped += (_, _) => sessionStopped = true;

        AssertTrue(windBotService.TryGetSession(botSession.InstanceId, out WindBot.WindBotSession found));
        AssertTrue(ReferenceEquals(botSession, found));
        AssertEqual(2, serverHost.ClientCount);

        // 等待本地客户端收到 Bot 进入房间及准备状态变更（玩家 1 进入 + Ready）
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        bool sawBotReady = false;
        while (DateTime.UtcNow < deadline && !sawBotReady)
        {
            observerClient.Update();
            sawBotReady = observerPackets.Any(p =>
                p.Length >= 2 &&
                p[0] == (byte)StocMessage.HsPlayerChange &&
                p[1] == ((1 << 4) | (byte)PlayerChange.Ready));
            if (!sawBotReady)
                await Task.Delay(10);
        }

        AssertTrue(sawBotReady);
        AssertTrue(sessionStarted || botSession.IsConnected);

        // 通过 C# 会话句柄直接发送聊天，验证本地客户端收到 StocMessage.Chat
        botSession.Chat("hello in-memory");
        bool sawChat = false;
        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && !sawChat)
        {
            observerClient.Update();
            sawChat = observerPackets.Any(p => p.Length > 3 && p[0] == (byte)StocMessage.Chat);
            if (!sawChat)
                await Task.Delay(10);
        }
        AssertTrue(sawChat);

        // 停止所有 Bot 会话并确认清理干净
        await windBotService.StopAllAsync();
        AssertTrue(sessionStopped);
        AssertFalse(botSession.IsRunning);
        AssertTrue(botSession.LastError is null);
        AssertEqual(0, windBotService.GetActiveSessions().Count);

        deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline && serverHost.ClientCount > 1)
            await Task.Delay(10);
        AssertEqual(1, serverHost.ClientCount);

        observerClient.Close();
    }
    finally
    {
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static void WindBotDirectClientProducesCtosPacketsSynchronously()
{
    string previousDirectory = Environment.CurrentDirectory;
    string temporaryDirectory = Path.Combine(Path.GetTempPath(), "YGOProSharpDirectBot_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(temporaryDirectory, "Decks"));

    try
    {
        File.WriteAllLines(Path.Combine(temporaryDirectory, "Decks", "AI_Yubel.ydk"),
        [
            "1",
            "!side",
            "2"
        ]);
        Environment.CurrentDirectory = temporaryDirectory;

        INamedCardRepository botCards = TestNamedRepository(TestNamedCard(1), TestNamedCard(2));
        WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = botCards
        });

        List<CtosMessage> producedEvents = new();
        using WindBot.WindBotDirectClient directClient = service.CreateDirectClient(new WindBot.WindBotInfo
        {
            Name = "DirectBot",
            Deck = "AI_Yubel",
            Chat = false
        });
        directClient.CtosPacketProduced += pkt => producedEvents.Add((CtosMessage)pkt.Span[0]);

        directClient.Start(pumpInitialHandshake: true);
        AssertTrue(directClient.IsStarted);
        AssertTrue(directClient.IsConnected);

        IReadOnlyList<byte[]> handshakePackets = directClient.DrainCtosPackets();
        AssertEqual(3, handshakePackets.Count);
        AssertEqual((byte)CtosMessage.ExternalAddress, handshakePackets[0][0]);
        AssertEqual((byte)CtosMessage.PlayerInfo, handshakePackets[1][0]);
        AssertEqual((byte)CtosMessage.JoinGame, handshakePackets[2][0]);
        AssertEqual(3, producedEvents.Count);

        // 同步喂入 JoinGame + TypeChange（Host），无需后台线程即可立刻拿到 UpdateDeck + HsReady
        using (MemoryStream joinPayload = new())
        using (BinaryWriter writer = new(joinPayload, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0U);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)5);
            directClient.FeedStocPacket(StocMessage.JoinGame, joinPayload.ToArray());
        }
        directClient.FeedStocPacket(StocMessage.TypeChange, [(byte)PlayerType.Host]);

        IReadOnlyList<byte[]> readyPackets = directClient.DrainCtosPackets();
        AssertTrue(readyPackets.Any(p => p[0] == (byte)CtosMessage.UpdateDeck));
        AssertTrue(readyPackets.Any(p => p[0] == (byte)CtosMessage.HsReady));

        // 喂入 SelectHand，验证同步产出 HandResult
        directClient.FeedStocPacket(StocMessage.SelectHand);
        AssertTrue(directClient.TryDequeueCtosPacket(out byte[] handPacket));
        AssertEqual((byte)CtosMessage.HandResult, handPacket[0]);

        // 直接发起聊天与投降
        directClient.Chat("gg");
        directClient.Surrender();
        IReadOnlyList<byte[]> controlPackets = directClient.DrainCtosPackets();
        AssertEqual(2, controlPackets.Count);
        AssertEqual((byte)CtosMessage.Chat, controlPackets[0][0]);
        AssertEqual((byte)CtosMessage.Surrender, controlPackets[1][0]);
    }
    finally
    {
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static void WindBotBundledDecksAndDialogsAssetsLoadFromOutputDirectory()
{
    string decksDir = Path.Combine(AppContext.BaseDirectory, "Decks");
    string dialogsDir = Path.Combine(AppContext.BaseDirectory, "Dialogs");

    AssertTrue(Directory.Exists(decksDir));
    AssertTrue(Directory.Exists(dialogsDir));

    string[] deckFiles = Directory.GetFiles(decksDir, "*.ydk");
    string[] dialogFiles = Directory.GetFiles(dialogsDir, "*.json");
    AssertTrue(deckFiles.Length >= 71);
    AssertTrue(dialogFiles.Length >= 20);

    // 验证通过 WindBotRuntime.ReadFile 直接解析内置 .ydk 文件
    using (FileStream oldSchool = WindBot.WindBotRuntime.ReadFile("Decks", "AI_OldSchool", "ydk"))
    {
        AssertTrue(oldSchool.Length > 0);
    }

    // 验证全部内置 Dialogs JSON 均可正常反序列化
    foreach (string dialogFile in dialogFiles)
    {
        string dialogName = Path.GetFileNameWithoutExtension(dialogFile);
        WindBot.Game.GameClient client = new(new WindBot.WindBotInfo
        {
            Dialog = dialogName,
            Chat = false
        }, TestNamedRepository());

        WindBot.Game.AI.Dialogs dialogs = new(client);
        dialogs.SendWelcome();
    }
}

static async Task WindBotServiceStartBotCapturesStartupErrorAsync()
{
    await using WindBot.WindBotService service = new(new WindBot.WindBotRuntimeOptions
    {
        CardRepository = TestNamedRepository(),
        TickDelayMilliseconds = 5
    });

    (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) = InMemoryNetworkTransport.CreatePair();
    using WindBot.WindBotSession session = service.StartBot(
        new WindBot.WindBotInfo
        {
            Name = "BrokenDeckBot",
            Deck = "Yubel",
            DeckFile = "missing_deck_" + Guid.NewGuid().ToString("N")
        },
        clientTransport);

    await WaitAsync(session.Completion.ContinueWith(_ => true));
    AssertFalse(session.IsRunning);
    AssertTrue(session.LastError is InvalidOperationException);
    AssertEqual(0, service.GetActiveSessions().Count);
    AssertFalse(serverTransport.IsConnected);

    bool threw = false;
    try
    {
        await session.WaitForCompletionAsync(throwOnError: true);
    }
    catch (InvalidOperationException)
    {
        threw = true;
    }
    AssertTrue(threw);
}

static List<string> CaptureDialogChatMessages(
    WindBot.Game.GameClient client,
    InMemoryNetworkTransport clientTransport,
    InMemoryNetworkTransport serverTransport,
    Action<WindBot.Game.GameClient> invokeDialogs)
{
    List<string> messages = new();
    YGOClient serverPeer = new(serverTransport);
    serverPeer.PacketReceivedRaw += packet =>
    {
        if (packet.Length > 1 && packet.Span[0] == (byte)CtosMessage.Chat)
        {
            string text = Encoding.Unicode.GetString(packet.Span.Slice(1)).TrimEnd('\0');
            messages.Add(text);
        }
    };

    client.Start(clientTransport);
    // 排空初始握手包（ExternalAddress / PlayerInfo / JoinGame）
    serverPeer.Update();
    messages.Clear();

    invokeDialogs(client);
    serverPeer.Update();
    serverPeer.Close();
    return messages;
}

static void WindBotLuaDialogXmakeStyleDslSupportsScopesFallbackAndDynamicHooks()
{
    string previousDirectory = Environment.CurrentDirectory;
    string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDialogDsl_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(tempDir, "Dialogs"));
    File.WriteAllText(
        Path.Combine(tempDir, "Dialogs", "base-fallback.json"),
        """
        {
          "welcome": ["FALLBACK_WELCOME"],
          "deckerror": ["FALLBACK_DECK_ERROR:{0}"],
          "endturn": ["FALLBACK_END_TURN"],
          "surrender": ["FALLBACK_SURRENDER"],
          "facedownmonstername": "默认里侧怪兽"
        }
        """);

    int initCalls = 0;
    int disposeCalls = 0;
    bool initSawMergedFallback = false;

    FakeLuaRuntime runtime = new(context =>
    {
        var dialogFn = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["dialog"]!;
        var dialogEndFn = (WindBot.Game.AI.Lua.LuaDslAction0)context.Globals["dialog_end"]!;
        var setLocale = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_locale"]!;
        var setDescription = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_description"]!;
        var setAuthor = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_author"]!;
        var setFallback = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_fallback"]!;
        var setFacedown = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["set_facedown_monster_name"]!;
        var addWelcome = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_welcome"]!;
        var setSummon = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["set_summon"]!;
        var addAttack = (WindBot.Game.AI.Lua.LuaDslAction3)context.Globals["add_attack"]!;
        var clearLines = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["clear_lines"]!;
        var onInit = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_init"]!;
        var onSummon = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_summon"]!;
        var onDirectAttack = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_directattack"]!;
        var onCustom = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_custom"]!;
        var onMessage = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_message"]!;
        var onDispose = (WindBot.Game.AI.Lua.LuaDslAction1)context.Globals["on_dispose"]!;

        // 顶层共享设置
        setFacedown("顶层里侧怪兽");

        dialogFn("PersonaAlpha");
            addWelcome("ALPHA_ONLY", null, null);
        dialogEndFn();

        dialogFn("PersonaBeta");
            setLocale("zh-CN");
            setDescription("xmake-style persona");
            setAuthor("tester");
            setFallback("base-fallback");
            addWelcome("BETA_WELCOME", null, null);
            setSummon("STATIC_SUMMON:{0}", null, null);
            addAttack("{0}->ATK->{1}", null, null);
            // 清空 endturn，验证显式 clear_lines 阻止回退继承
            clearLines("endturn");
            // 延迟解析函数名
            onSummon("format_beta_summon");
            onDirectAttack(new FakeRawLuaClosure(args =>
            {
                string attacker = (string)args[1]!;
                return attacker == "Silent" ? "" : null;
            }));
            onCustom(new FakeRawLuaClosure(args => $"CUSTOM#{args[1]}:{args[2]}"));
            onMessage(new FakeRawLuaClosure(args =>
            {
                string eventName = (string)args[1]!;
                string text = (string)args[2]!;
                return eventName == "welcome" ? "[PREFIX]" + text : null;
            }));
            onInit(new FakeRawLuaClosure(args =>
            {
                initCalls++;
                var ctx = (WindBot.Game.AI.Lua.LuaDialogContext)args[0]!;
                initSawMergedFallback = ctx.GetLines("surrender").Contains("FALLBACK_SURRENDER");
                return null;
            }));
            onDispose(new FakeRawLuaClosure(_ =>
            {
                disposeCalls++;
                return null;
            }));
        dialogEndFn();

        // 在脚本尾部定义具名回调
        context.Functions["format_beta_summon"] = new FakeLuaFunction(args =>
        {
            string monster = (string)args[1]!;
            return monster == "BlueEyes" ? "SPECIAL_BLUE_EYES" : null;
        });
    });

    WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider provider = new();
    provider.Add("PersonaBeta", "-- xmake dialog dsl");

    try
    {
        Environment.CurrentDirectory = tempDir;
        WindBot.Game.AI.DialogsManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ConfigureLua(runtime, provider);

        (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
            InMemoryNetworkTransport.CreatePair();
        using WindBot.Game.GameClient client = new(
            new WindBot.WindBotInfo { Name = "BetaBot", Deck = "AI_OldSchool", Dialog = "PersonaBeta", Chat = true, ChatIntervalMilliseconds = 0, DuelStartChatDelayMilliseconds = 0 },
            TestNamedRepository(),
            clientTransport);

        WindBot.Game.AI.Dialogs? dialogs = null;
        List<string> chats = CaptureDialogChatMessages(client, clientTransport, serverTransport, c =>
        {
            WindBot.Game.Duel duel = new();
            duel.Turn = 3;
            dialogs = new WindBot.Game.AI.Dialogs(c, duel);

            AssertTrue(dialogs.LuaScript != null);
            AssertEqual("PersonaBeta", dialogs.LuaScript!.Dsl.DeclaredDialogName);
            AssertEqual("zh-CN", dialogs.LuaScript.Dsl.DeclaredLocale);
            AssertEqual("xmake-style persona", dialogs.LuaScript.Dsl.DeclaredDescription);
            AssertEqual("tester", dialogs.LuaScript.Dsl.DeclaredAuthor);
            AssertEqual("顶层里侧怪兽", dialogs.FacedownMonsterName);
            AssertEqual(2, initCalls); // 1 次来自 GameBehavior 内 GameAI，1 次来自显式 dialogs
            AssertTrue(initSawMergedFallback);

            dialogs.SendWelcome();
            dialogs.SendDeckSorry("BadCard");
            dialogs.SendEndTurn();
            dialogs.SendSummon("BlueEyes");
            dialogs.SendSummon("OtherMonster");
            dialogs.SendAttack("Dragon", "monster");
            dialogs.SendDirectAttack("Silent");
            dialogs.SendCustomChat(2, "OK");
        });

        AssertEqual(6, chats.Count);
        AssertEqual("[PREFIX]BETA_WELCOME", chats[0]);
        AssertEqual("FALLBACK_DECK_ERROR:BadCard", chats[1]);
        AssertEqual("SPECIAL_BLUE_EYES", chats[2]);
        AssertEqual("STATIC_SUMMON:OtherMonster", chats[3]);
        AssertEqual("Dragon->ATK->顶层里侧怪兽", chats[4]);
        AssertEqual("CUSTOM#2:OK", chats[5]);

        dialogs!.Dispose();
        client.Dispose();
        AssertEqual(2, disposeCalls);
        AssertTrue(dialogs.LuaScript == null);
        // 确认 XLua 释放安全：全部上下文与 ILuaFunction 均已释放
        AssertTrue(runtime.CreatedContexts.All(c => c.Disposed));
        AssertTrue(runtime.CreatedContexts.All(c => c.TrackedFunctions.All(f => f.Disposed)));
    }
    finally
    {
        WindBot.Game.AI.DialogsManager.ResetLua();
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(tempDir, recursive: true);
    }
}

static void WindBotLuaDialogSupportsModuleTablePriorityAndErrorIsolation()
{
    string previousDirectory = Environment.CurrentDirectory;
    string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpLuaDialogMod_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(tempDir, "Dialogs"));
    File.WriteAllText(
        Path.Combine(tempDir, "Dialogs", "shared.json"),
        """
        {
          "welcome": ["JSON_WELCOME"],
          "summon": ["JSON_SUMMON:{0}"]
        }
        """);

    List<FakeLuaTable> createdTables = new();
    FakeLuaRuntime runtime = new(context =>
    {
        FakeLuaTable table = new();
        table.Fields["welcome"] = new[] { "LUA_TABLE_WELCOME" };
        table.Fields["summon"] = new[] { "LUA_TABLE_SUMMON:{0}" };
        table.Functions["OnSummon"] = new FakeLuaFunction(_ => throw new InvalidOperationException("boom in dialog hook"));
        createdTables.Add(table);
        context.ModuleResult = table;
    });

    WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider provider = new();
    provider.Add("shared", "-- return module table");

    try
    {
        Environment.CurrentDirectory = tempDir;
        WindBot.Game.AI.DialogsManager.ResetLua();

        // 1. 默认 PreferLuaDialogs=false 且无 AllowOverride 时，同名优先使用 JSON
        WindBot.Game.AI.DialogsManager.ConfigureLua(runtime, provider, preferLuaDialogs: false, throwOnScriptError: false);
        WindBot.Game.GameClient jsonClient = new(new WindBot.WindBotInfo { Dialog = "shared", Chat = true }, TestNamedRepository());
        using (WindBot.Game.AI.Dialogs jsonDialogs = new(jsonClient))
        {
            AssertTrue(jsonDialogs.LuaScript == null);
            AssertEqual("JSON_WELCOME", jsonDialogs.GetLines("welcome")[0]);
        }

        // 2. 注册 AllowOverride=true 描述符后，优先走 Lua 模块表，且异常在 throwOnScriptError=false 时隔离并回退静态台词
        WindBot.Game.AI.DialogsManager.RegisterLuaDialog(
            new WindBot.Game.AI.Lua.LuaDialogDescriptor("shared", "shared", allowOverride: true));
        (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
            InMemoryNetworkTransport.CreatePair();
        using WindBot.Game.GameClient luaClient = new(
            new WindBot.WindBotInfo { Deck = "AI_OldSchool", Dialog = "shared", Chat = true, ChatIntervalMilliseconds = 0, DuelStartChatDelayMilliseconds = 0 },
            TestNamedRepository(),
            clientTransport);

        List<string> chats = CaptureDialogChatMessages(luaClient, clientTransport, serverTransport, c =>
        {
            using WindBot.Game.AI.Dialogs luaDialogs = new(c);
            AssertTrue(luaDialogs.LuaScript != null);
            luaDialogs.SendWelcome();
            luaDialogs.SendSummon("DarkMagician");
        });

        AssertEqual(2, chats.Count);
        AssertEqual("LUA_TABLE_WELCOME", chats[0]);
        AssertEqual("LUA_TABLE_SUMMON:DarkMagician", chats[1]);

        luaClient.Dispose();
        AssertTrue(createdTables.Count > 0 && createdTables.All(t => t.Disposed));

        // 3. throwOnScriptError=true 时向上抛出异常
        FakeLuaTable throwingTable = new();
        throwingTable.Functions["OnWelcome"] = new FakeLuaFunction(_ => throw new InvalidOperationException("strict error"));
        FakeLuaRuntime strictRuntime = new(ctx => ctx.ModuleResult = throwingTable);
        WindBot.Game.AI.DialogsManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ConfigureLua(strictRuntime, provider, preferLuaDialogs: true, throwOnScriptError: true);

        using (WindBot.Game.AI.Dialogs strictDialogs = new(jsonClient))
        {
            bool threw = false;
            try
            {
                strictDialogs.SendWelcome();
            }
            catch (InvalidOperationException ex)
            {
                threw = ex.Message.Contains("strict error", StringComparison.Ordinal);
            }

            AssertTrue(threw);
        }

        // 4. 验证 GameBehavior 在加载卡组失败时也会释放 Lua Dialogs 资源
        FakeLuaTable failDeckDialogTable = new();
        failDeckDialogTable.Functions["OnWelcome"] = new FakeLuaFunction(_ => "hi");
        FakeLuaRuntime failDeckRuntime = new(ctx => ctx.ModuleResult = failDeckDialogTable);
        WindBot.Game.AI.DialogsManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ConfigureLua(failDeckRuntime, provider, preferLuaDialogs: true);

        WindBot.Game.GameClient missingDeckClient = new(
            new WindBot.WindBotInfo { Deck = "AI_OldSchool", DeckFile = "NonExistentDeckForDialogDispose", Dialog = "shared" },
            TestNamedRepository());
        bool deckThrew = false;
        try
        {
            _ = new WindBot.Game.GameBehavior(missingDeckClient, TestNamedRepository());
        }
        catch (InvalidOperationException)
        {
            deckThrew = true;
        }

        AssertTrue(deckThrew);
        AssertTrue(failDeckDialogTable.Disposed);
        AssertTrue(failDeckRuntime.CreatedContexts.All(c => c.Disposed));
    }
    finally
    {
        WindBot.Game.AI.DialogsManager.ResetLua();
        Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(tempDir, recursive: true);
    }
}

static void CliNLuaRuntimeExecutesExampleBlueEyesDialogEndToEnd()
{
    string root = FindRepositoryRoot();
    string luaDialogsDir = Path.Combine(root, "YGOProSharp.WindBot", "Dialogs", "Lua");
    INamedCardRepository cardRepo = TestNamedRepository(TestNamedCard(89631139));

    // 1. 验证 CLI 选项装配 LuaDialogDir / PreferLuaDialogs / EnableLuaDialogs / Locale
    CliConfiguration cliConfig = CliConfiguration.Load([
        $"LuaDialogDir={luaDialogsDir}",
        "Locale=zh-CN",
        "PreferLuaDialogs=true",
        "ThrowOnLuaScriptError=true"
    ]);
    WindBot.WindBotRuntimeOptions runtimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(cliConfig, cardRepo);
    AssertTrue(runtimeOptions.LuaRuntime is NLuaRuntime);
    AssertTrue(runtimeOptions.LuaDialogScriptProvider is WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider);
    AssertTrue(runtimeOptions.PreferLuaDialogs);
    AssertEqual("zh-CN", runtimeOptions.Locale);

    CliConfiguration disabledDialogs = CliConfiguration.Load(["EnableLuaDecks=false", "EnableLuaDialogs=false"]);
    WindBot.WindBotRuntimeOptions disabledOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(disabledDialogs, cardRepo);
    AssertTrue(disabledOptions.LuaRuntime is null);
    AssertTrue(disabledOptions.LuaDialogScriptProvider is null);

    try
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ResetLua();
        WindBot.WindBotRuntime.Configure(runtimeOptions);

        // 2. 简体中文（zh-CN）：验证 locale("zh-CN", "zh") 块 + set_fallback("auto") 命中 zh-CN.json + 动态青眼台词
        (InMemoryNetworkTransport clientTransportZh, InMemoryNetworkTransport serverTransportZh) =
            InMemoryNetworkTransport.CreatePair();
        using WindBot.Game.GameClient clientZh = new(
            new WindBot.WindBotInfo
            {
                Name = "BlueEyesBotZh",
                Deck = "AI_OldSchool",
                Dialog = "ExampleBlueEyes",
                Locale = "zh-CN",
                Chat = true,
                ChatIntervalMilliseconds = 0,
                DuelStartChatDelayMilliseconds = 0
            },
            cardRepo,
            clientTransportZh);

        List<string> chatsZh = CaptureDialogChatMessages(clientZh, clientTransportZh, serverTransportZh, c =>
        {
            WindBot.Game.Duel duel = new();
            duel.Turn = 1;
            using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

            AssertTrue(dialogs.LuaScript != null);
            AssertEqual("ExampleBlueEyes.zh-CN.lua", dialogs.ResolvedDialogName);
            AssertEqual("ExampleBlueEyes", dialogs.LuaScript!.Dsl.DeclaredDialogName);
            AssertEqual("zh-CN", dialogs.LuaScript.Dsl.DeclaredLocale);
            AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor); // 来自 includes("ExampleBlueEyes.common.lua")
            AssertEqual("zh-CN", dialogs.Locale);
            AssertEqual("里侧怪兽", dialogs.FacedownMonsterName);
            AssertTrue(dialogs.GetLines("deckerror").Any(line => line.Contains("卡组", StringComparison.Ordinal)));

            dialogs.SendNewTurn();
            dialogs.SendSummon("青眼白龙");
            dialogs.SendSummon("太古的白石");
            dialogs.SendDirectAttack("青眼白龙");
            dialogs.SendAttack("青眼亚白龙", "monster");
        });

        AssertEqual(5, chatsZh.Count);
        AssertEqual("第一回合，抽卡！让青眼的传说就此开始！", chatsZh[0]);
        AssertEqual("出现吧，最强之龙——青眼白龙！", chatsZh[1]);
        AssertTrue(chatsZh[2].Contains("太古的白石", StringComparison.Ordinal));
        AssertEqual("青眼白龙，直接攻击！毁灭的爆裂疾风弹！！", chatsZh[3]);
        AssertTrue(chatsZh[4].Contains("青眼亚白龙", StringComparison.Ordinal) && chatsZh[4].Contains("里侧怪兽", StringComparison.Ordinal));

        // 3. 繁体中文（zh-TW）：验证自动命中拆分文件 ExampleBlueEyes.zh-TW.lua
        (InMemoryNetworkTransport clientTransportTw, InMemoryNetworkTransport serverTransportTw) =
            InMemoryNetworkTransport.CreatePair();
        using WindBot.Game.GameClient clientTw = new(
            new WindBot.WindBotInfo
            {
                Name = "BlueEyesBotTw",
                Deck = "AI_OldSchool",
                Dialog = "ExampleBlueEyes",
                Locale = "zh-TW",
                Chat = true,
                ChatIntervalMilliseconds = 0,
                DuelStartChatDelayMilliseconds = 0
            },
            cardRepo,
            clientTransportTw);

        List<string> chatsTw = CaptureDialogChatMessages(clientTw, clientTransportTw, serverTransportTw, c =>
        {
            WindBot.Game.Duel duel = new();
            duel.Turn = 1;
            using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

            AssertEqual("ExampleBlueEyes.zh-TW.lua", dialogs.ResolvedDialogName);
            AssertEqual("zh-TW", dialogs.LuaScript!.Dsl.DeclaredLocale);
            AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor);
            AssertEqual("裡側怪獸", dialogs.FacedownMonsterName);
            dialogs.SendNewTurn();
            dialogs.SendSummon("青眼白龍");
        });

        AssertEqual(2, chatsTw.Count);
        AssertEqual("第一回合，抽牌！讓青眼的傳說就此開始！", chatsTw[0]);
        AssertEqual("出現吧，最強之龍——青眼白龍！", chatsTw[1]);

        // 4. 英文（en-US）：验证自动命中英文基线 ExampleBlueEyes.lua + default.json 回退
        (InMemoryNetworkTransport clientTransportEn, InMemoryNetworkTransport serverTransportEn) =
            InMemoryNetworkTransport.CreatePair();
        using WindBot.Game.GameClient clientEn = new(
            new WindBot.WindBotInfo
            {
                Name = "BlueEyesBotEn",
                Deck = "AI_OldSchool",
                Dialog = "ExampleBlueEyes",
                Locale = "en-US",
                Chat = true,
                ChatIntervalMilliseconds = 0,
                DuelStartChatDelayMilliseconds = 0
            },
            cardRepo,
            clientTransportEn);

        List<string> chatsEn = CaptureDialogChatMessages(clientEn, clientTransportEn, serverTransportEn, c =>
        {
            WindBot.Game.Duel duel = new();
            duel.Turn = 1;
            using WindBot.Game.AI.Dialogs dialogs = new(c, duel);

            AssertEqual("ExampleBlueEyes.lua", dialogs.ResolvedDialogName);
            AssertEqual("en-US", dialogs.LuaScript!.Dsl.DeclaredLocale);
            AssertEqual("YGOProSharp", dialogs.LuaScript.Dsl.DeclaredAuthor);
            AssertEqual("monster", dialogs.FacedownMonsterName);
            AssertTrue(dialogs.GetLines("deckerror").Any(line => line.Contains("deck", StringComparison.OrdinalIgnoreCase)));
            dialogs.SendNewTurn();
            dialogs.SendSummon("Blue-Eyes White Dragon");
            dialogs.SendDirectAttack("Blue-Eyes White Dragon");
        });

        AssertEqual(3, chatsEn.Count);
        AssertEqual("Turn 1, draw! Let the legend of Blue-Eyes begin!", chatsEn[0]);
        AssertEqual("Come forth, the ultimate engine of destruction — Blue-Eyes White Dragon!", chatsEn[1]);
        AssertEqual("Blue-Eyes White Dragon, direct attack! Burst Stream of Destruction!!", chatsEn[2]);
    }
    finally
    {
        WindBot.WindBotRuntime.Locale = null;
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ResetLua();
    }
}

static void WindBotDialogsSupportsLocalizationAndConsolidatedFolders()
{
    INamedCardRepository cardRepo = TestNamedRepository(TestNamedCard(89631139));

    try
    {
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ResetLua();
        WindBot.WindBotRuntime.Configure(new WindBot.WindBotRuntimeOptions
        {
            CardRepository = cardRepo,
            LuaRuntime = NLuaRuntime.Instance
        });

        // 1. 验证默认无参数配置即可从输出目录集中位置自动发现 Decks/Lua 与 Dialogs/Lua 脚本
        WindBot.Game.AI.Lua.FileSystemLuaDeckScriptProvider defaultDeckProvider = new();
        AssertTrue(defaultDeckProvider.TryGetScriptSource("ExampleBlueEyes", out string? deckSrc, out _));
        AssertTrue(!string.IsNullOrWhiteSpace(deckSrc));

        WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider defaultDialogProvider = new();
        AssertTrue(defaultDialogProvider.TryGetScriptSource("ExampleBlueEyes", out string? dialogSrc, out _));
        AssertTrue(!string.IsNullOrWhiteSpace(dialogSrc));

        // 2. JSON 自动本地化：Dialog="cirno" + Locale="zh-CN" -> 自动命中 cirno.zh-CN.json
        WindBot.Game.GameClient cirnoClient = new(
            new WindBot.WindBotInfo { Dialog = "cirno", Locale = "zh-CN", Chat = true },
            cardRepo);
        using (WindBot.Game.AI.Dialogs cirnoDialogs = new(cirnoClient))
        {
            AssertEqual("cirno.zh-CN", cirnoDialogs.ResolvedDialogName);
            AssertTrue(cirnoDialogs.GetLines("welcome").Any(line => line.Contains("最强", StringComparison.Ordinal)));
        }

        // 3. JSON 默认本地化：Dialog="default" + Locale="zh-CN" -> 自动命中 zh-CN.json
        WindBot.Game.GameClient defaultZhClient = new(
            new WindBot.WindBotInfo { Dialog = "default", Locale = "zh-CN", Chat = true },
            cardRepo);
        using (WindBot.Game.AI.Dialogs defaultZhDialogs = new(defaultZhClient))
        {
            AssertEqual("zh-CN", defaultZhDialogs.ResolvedDialogName);
            AssertTrue(defaultZhDialogs.GetLines("welcome").Any(line => line.Contains("机器人", StringComparison.Ordinal)));
        }

        // 4. 拆分文件本地化：优先命中 <name>.<locale>.lua，未命中时回退 <name>.lua + is_locale(...)
        WindBot.Game.AI.Lua.InMemoryLuaDialogScriptProvider splitProvider = new();
        splitProvider.Add("SplitHero", """
            dialog("SplitHero")
                if is_locale("ja") then
                    add_welcome("こんにちは、ヒーローだ！")
                else
                    add_welcome("Hello from base SplitHero!")
                end
            dialog_end()
            """);
        splitProvider.Add("SplitHero.zh-CN", """
            dialog("SplitHero.zh-CN")
                add_welcome("你好，来自拆分中文文件 SplitHero.zh-CN.lua！")
            dialog_end()
            """);

        WindBot.Game.AI.DialogsManager.ConfigureLua(NLuaRuntime.Instance, splitProvider, preferLuaDialogs: true, throwOnScriptError: true);

        using (WindBot.Game.AI.Dialogs zhSplit = new(new WindBot.Game.GameClient(
            new WindBot.WindBotInfo { Dialog = "SplitHero", Locale = "zh-CN" }, cardRepo)))
        {
            AssertEqual("SplitHero.zh-CN.lua", zhSplit.ResolvedDialogName);
            AssertEqual("你好，来自拆分中文文件 SplitHero.zh-CN.lua！", zhSplit.GetLines("welcome")[0]);
        }

        using (WindBot.Game.AI.Dialogs jaFallback = new(new WindBot.Game.GameClient(
            new WindBot.WindBotInfo { Dialog = "SplitHero", Locale = "ja-JP" }, cardRepo)))
        {
            AssertEqual("SplitHero.lua", jaFallback.ResolvedDialogName);
            AssertEqual("こんにちは、ヒーローだ！", jaFallback.GetLines("welcome")[0]);
        }

        // 5. 跨 .lua 的 set_fallback 与子目录 Dialogs/Lua/<name>/<locale>.lua 解析
        string nestedTempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpNestedLuaDialogs_" + Guid.NewGuid().ToString("N"));
        try
        {
            string charDir = Path.Combine(nestedTempDir, "MyChar");
            Directory.CreateDirectory(charDir);
            File.WriteAllText(Path.Combine(nestedTempDir, "BasePersona.lua"), """
                dialog("BasePersona")
                    add_surrender("BASE_LUA_SURRENDER")
                dialog_end()
                """);
            File.WriteAllText(Path.Combine(charDir, "zh-CN.lua"), """
                dialog("MyChar")
                    set_fallback("BasePersona")
                    add_welcome("NESTED_ZH_WELCOME")
                dialog_end()
                """);

            WindBot.Game.AI.DialogsManager.ConfigureLua(
                NLuaRuntime.Instance,
                new WindBot.Game.AI.Lua.FileSystemLuaDialogScriptProvider(nestedTempDir),
                preferLuaDialogs: true,
                throwOnScriptError: true);

            using WindBot.Game.AI.Dialogs nestedDialog = new(new WindBot.Game.GameClient(
                new WindBot.WindBotInfo { Dialog = "MyChar", Locale = "zh-CN" }, cardRepo));
            AssertEqual("NESTED_ZH_WELCOME", nestedDialog.GetLines("welcome")[0]);
            AssertEqual("BASE_LUA_SURRENDER", nestedDialog.GetLines("surrender")[0]);
        }
        finally
        {
            Directory.Delete(nestedTempDir, recursive: true);
        }
    }
    finally
    {
        WindBot.WindBotRuntime.Locale = null;
        WindBot.Game.AI.DecksManager.ResetLua();
        WindBot.Game.AI.DialogsManager.ResetLua();
    }
}

static void WindBotChatPacerPacesBurstsAndRespectsDelay()
{
    (InMemoryNetworkTransport clientTransport, InMemoryNetworkTransport serverTransport) =
        InMemoryNetworkTransport.CreatePair();
    List<string> received = new();
    YGOClient serverPeer = new(serverTransport);
    serverPeer.PacketReceivedRaw += packet =>
    {
        if (packet.Length > 1 && packet.Span[0] == (byte)CtosMessage.Chat)
            received.Add(Encoding.Unicode.GetString(packet.Span.Slice(1)).TrimEnd('\0'));
    };

    using WindBot.Game.GameClient client = new(
        new WindBot.WindBotInfo
        {
            Name = "PacedBot",
            Deck = "AI_OldSchool",
            Dialog = "default",
            Chat = true,
            ChatIntervalMilliseconds = 60,
            DuelStartChatDelayMilliseconds = 80,
            MaxQueuedChatMessages = 3
        },
        TestNamedRepository(),
        clientTransport);

    client.Start(clientTransport);
    serverPeer.Update();
    received.Clear();

    // 1. 首条立即发送，后续密集 5 条按 MaxQueuedChatMessages=3 丢弃最旧并排队（保留 msg4, msg5, msg6）
    client.Chat("msg1");
    client.Chat("msg2");
    client.Chat("msg3");
    client.Chat("msg4");
    client.Chat("msg5");
    client.Chat("msg6");

    serverPeer.Update();
    AssertEqual(1, received.Count);
    AssertEqual("msg1", received[0]);
    AssertEqual(3, client.PendingChatCount);

    // 2. 逐次等待节流间隔并 Tick，按顺序逐条发出（不一次性倾倒）
    for (int i = 0; i < 3; i++)
    {
        Thread.Sleep(75);
        client.Tick();
        serverPeer.Update();
        AssertEqual(i + 2, received.Count);
    }

    AssertEqual(4, received.Count);
    AssertEqual("msg1", received[0]);
    AssertEqual("msg4", received[1]);
    AssertEqual("msg5", received[2]);
    AssertEqual("msg6", received[3]);
    AssertEqual(0, client.PendingChatCount);

    // 3. DelayChat 延后首条聊天，未到期不发，到期后发出
    received.Clear();
    client.DelayChat(90);
    client.Chat("after-delay");
    client.Tick();
    serverPeer.Update();
    AssertEqual(0, received.Count);

    Thread.Sleep(110);
    client.Tick();
    serverPeer.Update();
    AssertEqual(1, received.Count);
    AssertEqual("after-delay", received[0]);
    serverPeer.Close();
}

static async Task ServerBroadcastsChatToSenderAndOpponentAsync()
{
    ServerOptions serverOptions = new()
    {
        EnableNetworkListener = false,
        ClientVersion = 0x1362,
        Game = new GameOptions { NoCheckDeck = true }
    };

    await using YGOProSharpServerHost host = new(duelFactory: null, cardRepository: TestRepository(TestCard(1)), options: serverOptions);
    host.StartBackgroundLoop(tickDelayMilliseconds: 2, embedded: true);

    YGOClient playerA = host.ConnectLocalClient();
    YGOClient playerB = host.ConnectLocalClient();

    List<(short Sender, string Text)> receivedByA = new();
    List<(short Sender, string Text)> receivedByB = new();
    playerA.PacketReceivedRaw += p =>
    {
        if (p.Length > 3 && p.Span[0] == (byte)StocMessage.Chat)
        {
            short sender = BitConverter.ToInt16(p.Span.Slice(1, 2));
            string text = Encoding.Unicode.GetString(p.Span.Slice(3)).TrimEnd('\0');
            receivedByA.Add((sender, text));
        }
    };
    playerB.PacketReceivedRaw += p =>
    {
        if (p.Length > 3 && p.Span[0] == (byte)StocMessage.Chat)
        {
            short sender = BitConverter.ToInt16(p.Span.Slice(1, 2));
            string text = Encoding.Unicode.GetString(p.Span.Slice(3)).TrimEnd('\0');
            receivedByB.Add((sender, text));
        }
    };

    void JoinPlayer(YGOClient client, string name)
    {
        using (MemoryStream info = new())
        using (BinaryWriter w = new(info, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)CtosMessage.PlayerInfo);
            w.WriteUnicode(name, 20);
            client.Send(info.ToArray());
        }
        using (MemoryStream join = new())
        using (BinaryWriter w = new(join, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)CtosMessage.JoinGame);
            w.Write((short)0x1362);
            w.Write(0);
            w.Write((short)0);
            w.WriteUnicode("", 20);
            client.Send(join.ToArray());
        }
    }

    JoinPlayer(playerA, "PlayerA");
    JoinPlayer(playerB, "PlayerB");
    await Task.Delay(40);
    playerA.Update();
    playerB.Update();

    using (MemoryStream chat = new())
    using (BinaryWriter w = new(chat, Encoding.UTF8, leaveOpen: true))
    {
        w.Write((byte)CtosMessage.Chat);
        w.WriteUnicodeAutoLength("你好 MDPro3!", 255);
        playerA.Send(chat.ToArray());
    }

    DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (DateTime.UtcNow < deadline && (receivedByA.Count == 0 || receivedByB.Count == 0))
    {
        playerA.Update();
        playerB.Update();
        await Task.Delay(10);
    }

    AssertTrue(receivedByA.Any(x => x.Sender == 0 && x.Text == "你好 MDPro3!"));
    AssertTrue(receivedByB.Any(x => x.Sender == 0 && x.Text == "你好 MDPro3!"));

    playerA.Close();
    playerB.Close();
    await host.StopAsync();
}

static void CoreLayeredDiyResourcesOverrideCardsScriptsAndBanlists()
{
    string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpDiyCore_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);

    try
    {
        // 1. 多 CDB 覆盖 + 内存 DIY 卡片覆盖
        string baseDb = Path.Combine(tempDir, "cards.cdb");
        string diyDb = Path.Combine(tempDir, "diy.cdb");
        CreateTestCardDb(baseDb,
        [
            (1001, 1, 0, 0, (int)CardType.Monster, 4, 1, 1, 1800, 1000, "BaseOCG", "desc"),
            (1002, 2, 0, 0, (int)CardType.Monster, 4, 1, 1, 1700, 1000, "BaseTCG", "desc")
        ]);
        CreateTestCardDb(diyDb,
        [
            (1001, 3, 0, 0, (int)CardType.Monster, 4, 1, 1, 2500, 1200, "OverriddenBase", "override"),
            (9990001, 4, 0, 0, (int)CardType.Monster, 8, 1, 1, 3500, 2800, "DiyDragon", "custom")
        ]);

        SqliteCardDatabaseManager dbManager = new();
        ICardRepository layeredCards = dbManager.LoadCards([baseDb, diyDb]);
        AssertTrue(layeredCards.TryGetCard(1001, out Card overridden));
        AssertEqual(2500, overridden.Attack);
        AssertEqual(3, overridden.Ot);
        AssertTrue(layeredCards.TryGetCard(9990001, out Card diyCard));
        AssertEqual(4, diyCard.Ot);

        Card inMemoryCard = new(9990002, 4, 0, 0, (int)CardType.Spell, 0, 0, 0, 0, 0, 0, 0, 0);
        CompositeCardRepository compositeCards = new(new InMemoryCardRepository([inMemoryCard]), layeredCards);
        AssertTrue(compositeCards.TryGetCard(9990002, out Card memFound));
        AssertEqual((int)CardType.Spell, memFound.Type);
        AssertTrue(compositeCards.TryGetCard(1002, out _));

        // 2. DIY 卡片区域校验（Region=0/2 拒绝 ot=4，Region=3/5 放行 ot=4）
        Deck diyDeck = new(compositeCards);
        diyDeck.AddMain(9990001);
        DeckRules minOne = new(MainDeckMinSize: 1);
        AssertEqual(9990001, diyDeck.Check(null, ocg: true, tcg: true, allowCustom: false, minOne));
        AssertEqual(0, diyDeck.Check(null, ocg: true, tcg: true, allowCustom: true, minOne));
        AssertEqual((true, true, true), DefaultDeckValidator.ResolveRegionFlags(3));
        AssertEqual((false, false, false), DefaultDeckValidator.ResolveRegionFlags(4));

        // 3. 分层脚本（内存 > DIY 目录 > 基础目录 > .ypk 压缩包）+ 路径穿越保护
        string baseScriptDir = Path.Combine(tempDir, "script");
        string diyScriptDir = Path.Combine(tempDir, "diy", "script");
        Directory.CreateDirectory(baseScriptDir);
        Directory.CreateDirectory(diyScriptDir);
        File.WriteAllText(Path.Combine(baseScriptDir, "c1001.lua"), "-- base");
        File.WriteAllText(Path.Combine(diyScriptDir, "c1001.lua"), "-- diy dir");

        string ypkPath = Path.Combine(tempDir, "pack.ypk");
        using (ZipArchive archive = ZipFile.Open(ypkPath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("script/c9990001.lua");
            using StreamWriter sw = new(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            sw.Write("-- from ypk");
        }

        InMemoryScriptProvider memoryScripts = new();
        memoryScripts.RegisterText("c9990002.lua", "-- from memory");

        using CompositeScriptProvider compositeScripts = new(
            memoryScripts,
            new FileScriptProvider([diyScriptDir, baseScriptDir]),
            new ZipArchiveScriptProvider(ypkPath));

        AssertTrue(compositeScripts.TryGetScript("./script/c1001.lua", out byte[] s1));
        AssertEqual("-- diy dir", Encoding.UTF8.GetString(s1));
        AssertTrue(compositeScripts.TryGetScript("./script/c9990001.lua", out byte[] s2));
        AssertEqual("-- from ypk", Encoding.UTF8.GetString(s2));
        AssertTrue(compositeScripts.TryGetScript("c9990002.lua", out byte[] s3));
        AssertEqual("-- from memory", Encoding.UTF8.GetString(s3));
        AssertFalse(compositeScripts.TryGetScript("./script/../../secret.lua", out _));

        // 4. 多禁限表加载与名称/Hash 查询
        string baseLf = Path.Combine(tempDir, "lflist.conf");
        string diyLf = Path.Combine(tempDir, "diy_lflist.conf");
        File.WriteAllText(baseLf, "!2026.04 OCG\n1001 1\n");
        File.WriteAllText(diyLf, "!DIY Custom\n$whitelist\n9990001 3\n");

        BanlistManager.Init([diyLf, baseLf]);
        AssertEqual(2, BanlistManager.Banlists.Count);
        AssertEqual("DIY Custom", BanlistManager.Banlists[0].Name);
        AssertTrue(BanlistManager.Banlists[0].WhitelistOnly);
        AssertEqual(3, BanlistManager.Banlists[0].GetQuantity(9990001));
        AssertEqual(0, BanlistManager.Banlists[0].GetQuantity(1001));
        AssertTrue(BanlistManager.FindByName("2026.04 OCG") is not null);
        AssertTrue(BanlistManager.TryGetByHash(BanlistManager.Banlists[1].Hash, out Banlist? byHash));
        AssertEqual("2026.04 OCG", byHash!.Name);
    }
    finally
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }
}

static void RoomPasswordParserParsesBuiltInAndCustomDiyTokens()
{
    RoomPasswordParser parser = new();
    parser.RegisterToken(["BOSS"], static (o, _) =>
    {
        o.StartLp = 16000;
        o.Region = 3;
        o.ExtraScripts.Add("./script/boss_mode.lua");
        o.CustomTags["Mode"] = "Boss";
    });
    parser.RegisterPrefixToken("SKILL:", static (o, arg) => o.CustomTags["Skill"] = arg);

    ParsedRoomPassword complex = parser.Parse("M,DIY,LP4000,MR5,LF2,ST6,DR2,TM180,NC,NS,PR#DiyArena");
    AssertTrue(complex.HasRulePrefix);
    AssertEqual("DiyArena", complex.RoomName);
    GameOptions applied = complex.ApplyTo(new GameOptions());
    AssertEqual(1, applied.Mode);
    AssertEqual(3, applied.Region);
    AssertEqual(4000, applied.StartLp);
    AssertEqual(5, applied.MasterRule);
    AssertEqual(1, applied.Banlist);
    AssertEqual(6, applied.StartHand);
    AssertEqual(2, applied.DrawCount);
    AssertEqual(180, applied.GameTimer);
    AssertTrue(applied.NoCheckDeck);
    AssertTrue(applied.NoShuffleDeck);
    AssertTrue(applied.EnablePriority);

    ParsedRoomPassword tag = parser.Parse("T#TagRoom");
    GameOptions tagOpts = tag.ApplyTo(new GameOptions());
    AssertEqual(2, tagOpts.Mode);
    AssertEqual(16000, tagOpts.StartLp);

    ParsedRoomPassword bo5 = parser.Parse("BO5,NF#Championship");
    GameOptions bo5Opts = bo5.ApplyTo(new GameOptions());
    AssertEqual(1, bo5Opts.Mode);
    AssertEqual(3, bo5Opts.MatchWins);
    AssertEqual(-1, bo5Opts.Banlist);

    ParsedRoomPassword custom = parser.Parse("BOSS,SKILL:DestinyDraw#BossRoom");
    GameOptions customOpts = custom.ApplyTo(new GameOptions());
    AssertEqual(16000, customOpts.StartLp);
    AssertEqual(3, customOpts.Region);
    AssertTrue(customOpts.ExtraScripts.Contains("./script/boss_mode.lua"));
    AssertEqual("Boss", customOpts.CustomTags!["Mode"]);
    AssertEqual("DestinyDraw", customOpts.CustomTags["Skill"]);

    ParsedRoomPassword plain = parser.Parse("PlainRoom");
    AssertFalse(plain.HasRulePrefix);
    AssertEqual("PlainRoom", plain.RoomName);
}

static async Task ServerExtensionsSupportAddonsDeckValidatorChatCommandsAndDuelPreloadAsync()
{
    Card c1 = new(1001, 4, 0, 0, (int)CardType.Monster, 4, 1, 1, 1800, 1000, 0, 0, 0);
    InMemoryCardRepository repo = new([c1]);
    ServerTestFakeDuelFactory duelFactory = new();
    bool addonInitialized = false;
    bool addonDisposed = false;
    bool beforeDuelStartFired = false;
    bool allowDeck = false;

    ServerOptions options = new()
    {
        EnableNetworkListener = false,
        EnableChatCommands = true,
        ChatCommands =
        [
            new ChatCommandDefinition("ping", "测试命令", ctx => ctx.Reply($"PONG:{ctx.RawArguments}"))
        ],
        AddonFactories =
        [
            game => new ServerTestRecordingAddon(
                game,
                () => addonInitialized = true,
                () => addonDisposed = true,
                ctx =>
                {
                    beforeDuelStartFired = true;
                    ctx.Session.AddCard(9990001, 0, CardLocation.Extra);
                })
        ],
        Game = new GameOptions
        {
            Region = 3,
            DeckRules = new DeckRules(MainDeckMinSize: 1),
            ExtraScripts = ["./script/diy_rule.lua"],
            DeckValidator = new ServerTestDelegateDeckValidator(_ => allowDeck ? 0 : 1001)
        }
    };

    CoreServer server = new(duelFactory, repo, options);
    server.StartEmbedded();
    AssertTrue(addonInitialized);

    YGOClient p1 = server.ConnectLocalClient();
    YGOClient p2 = server.ConnectLocalClient();
    List<(short Sender, string Text)> p1Chats = new();
    List<(short Sender, string Text)> p2Chats = new();
    List<int> p1DeckErrors = new();

    void HookChat(YGOClient client, List<(short, string)> sink, List<int>? deckErrors)
    {
        client.PacketReceivedRaw += pkt =>
        {
            if (pkt.Length == 0)
                return;
            PacketReader r = new(pkt.Span);
            StocMessage msg = (StocMessage)r.ReadByte();
            if (msg == StocMessage.Chat)
            {
                short sender = r.ReadInt16();
                string text = r.ReadUnicodeAutoLength(256);
                sink.Add((sender, text));
            }
            else if (msg == StocMessage.ErrorMsg && deckErrors is not null)
            {
                byte errType = r.ReadByte();
                r.ReadSpan(3);
                int code = r.ReadInt32();
                if (errType == 2)
                    deckErrors.Add(code);
            }
        };
    }

    HookChat(p1, p1Chats, p1DeckErrors);
    HookChat(p2, p2Chats, null);

    SendPlayerJoin(p1, "Alice", "DIY,LP6000#");
    SendPlayerJoin(p2, "Bob", "DIY,LP6000#");
    PumpServerAndClients(server, p1, p2);

    AssertEqual(6000, server.Game!.StartLp);
    AssertEqual(3, server.Game.Region);

    // 1. 聊天命令应被拦截，只回执给发送者，不广播给对手
    SendChat(p1, "/ping hello");
    SendChat(p1, "/rules");
    PumpServerAndClients(server, p1, p2);
    AssertTrue(p1Chats.Any(c => c.Text == "PONG:hello"));
    AssertTrue(p1Chats.Any(c => c.Text.Contains("LP=6000") && c.Text.Contains("diy_rule.lua")));
    AssertEqual(0, p2Chats.Count);

    // 普通聊天正常广播
    SendChat(p1, "normal hello");
    PumpServerAndClients(server, p1, p2);
    AssertTrue(p2Chats.Any(c => c.Text == "normal hello"));

    // 2. 自定义 IDeckValidator 拦截不合法卡组并放行合法卡组
    SendSingleCardDeck(p1, 1001);
    SendSingleCardDeck(p2, 1001);
    SendSimpleCtos(p1, CtosMessage.HsReady);
    PumpServerAndClients(server, p1, p2);
    AssertTrue(p1DeckErrors.Contains(1001));
    AssertFalse(server.Game.IsReady[0]);

    allowDeck = true;
    SendSimpleCtos(p1, CtosMessage.HsReady);
    SendSimpleCtos(p2, CtosMessage.HsReady);
    PumpServerAndClients(server, p1, p2);
    SendSimpleCtos(p1, CtosMessage.HsStart);
    PumpServerAndClients(server, p1, p2);

    // 3. 进入猜拳（1=剪刀, 2=石头, 3=布）-> p1 胜出并选先攻 -> 触发 ExtraScripts.PreloadScript 与 OnBeforeDuelStart
    SendByteCtos(p1, CtosMessage.HandResult, 2); // Rock
    SendByteCtos(p2, CtosMessage.HandResult, 1); // Scissors -> p1 wins
    PumpServerAndClients(server, p1, p2);
    SendByteCtos(p1, CtosMessage.TpResult, 1);
    PumpServerAndClients(server, p1, p2);

    AssertTrue(beforeDuelStartFired);
    AssertTrue(duelFactory.LastSession is not null);
    AssertTrue(duelFactory.LastSession!.PreloadedScripts.Contains("./script/diy_rule.lua"));
    AssertTrue(duelFactory.LastSession.AddedCards.Any(c => c.CardId == 9990001 && c.Location == CardLocation.Extra));
    AssertTrue(duelFactory.LastSession.Started);

    server.Stop();
    AssertTrue(addonDisposed);
    await Task.CompletedTask;
}

static async Task CoreServerMultiRoomRoutesConcurrentRoomsAndCleansUpAsync()
{
    ServerOptions options = new()
    {
        EnableNetworkListener = false,
        EnableMultiRoom = true,
        EnableChatCommands = true
    };

    CoreServer server = new(options: options);
    server.StartEmbedded();

    List<string> createdRooms = new();
    List<string> removedRooms = new();
    server.OnRoomCreated += g => createdRooms.Add(g.RoomName);
    server.OnRoomRemoved += g => removedRooms.Add(g.RoomName);

    YGOClient p1 = server.ConnectLocalClient();
    YGOClient p2 = server.ConnectLocalClient();
    YGOClient p3 = server.ConnectLocalClient();

    List<string> p1Messages = new();
    p1.PacketReceivedRaw += pkt =>
    {
        PacketReader r = new(pkt.Span);
        if (r.Remaining > 0 && (StocMessage)r.ReadByte() == StocMessage.Chat)
        {
            r.ReadInt16();
            p1Messages.Add(r.ReadUnicodeAutoLength(256));
        }
    };

    SendPlayerJoin(p1, "Alice", "DIY,LP4000#Alpha");
    SendPlayerJoin(p2, "Bob", "Alpha");
    SendPlayerJoin(p3, "Charlie", "T#Beta");
    PumpServerAndClients(server, p1, p2, p3);

    AssertEqual(2, server.RoomCount);
    AssertTrue(createdRooms.Contains("Alpha"));
    AssertTrue(createdRooms.Contains("Beta"));
    AssertTrue(server.TryGetRoom("Alpha", out Game? alpha));
    AssertTrue(server.TryGetRoom("Beta", out Game? beta));
    AssertEqual(4000, alpha!.StartLp);
    AssertEqual(3, alpha.Region);
    AssertEqual(2, alpha.CreateSnapshot().DuelistCount);
    AssertEqual(2, beta!.Mode);
    AssertEqual(16000, beta.StartLp);
    AssertEqual(1, beta.CreateSnapshot().DuelistCount);

    SendChat(p1, "/rooms");
    PumpServerAndClients(server, p1, p2, p3);
    AssertTrue(p1Messages.Any(m => m.Contains("Alpha") && m.Contains("Beta")));

    // 房主 Charlie 离开 Beta，只关闭 Beta，不关闭整个服务器也不影响 Alpha
    SendSimpleCtos(p3, CtosMessage.LeaveGame);
    PumpServerAndClients(server, p1, p2, p3);

    AssertTrue(server.IsRunning);
    AssertEqual(1, server.RoomCount);
    AssertTrue(removedRooms.Contains("Beta"));
    AssertTrue(server.TryGetRoom("Alpha", out _));
    AssertFalse(server.TryGetRoom("Beta", out _));

    server.Stop();
    AssertEqual(0, server.RoomCount);
    await Task.CompletedTask;
}

static void ServerAutoDiscoversExpansionsAndCliMapsOptions()
{
    string tempDir = Path.Combine(Path.GetTempPath(), "YGOProSharpAutoExp_" + Guid.NewGuid().ToString("N"));
    string baseScriptDir = Path.Combine(tempDir, "script");
    string expDir = Path.Combine(tempDir, "expansions");
    string diyDir = Path.Combine(tempDir, "diy");
    string diyScriptDir = Path.Combine(diyDir, "script");
    Directory.CreateDirectory(baseScriptDir);
    Directory.CreateDirectory(expDir);
    Directory.CreateDirectory(diyScriptDir);

    try
    {
        CreateTestCardDb(Path.Combine(tempDir, "cards.cdb"),
            [(1001, 1, 0, 0, (int)CardType.Monster, 4, 1, 1, 1500, 1000, "Base", "d")]);
        CreateTestCardDb(Path.Combine(expDir, "exp.cdb"),
            [(1001, 3, 0, 0, (int)CardType.Monster, 4, 1, 1, 2000, 1000, "Exp", "d")]);
        CreateTestCardDb(Path.Combine(diyDir, "custom.cdb"),
            [(9990010, 4, 0, 0, (int)CardType.Monster, 4, 1, 1, 3000, 2000, "Diy", "d")]);

        File.WriteAllText(Path.Combine(tempDir, "lflist.conf"), "!BaseLF\n1001 1\n");
        File.WriteAllText(Path.Combine(diyDir, "lflist.conf"), "!DiyLF\n9990010 3\n");
        File.WriteAllText(Path.Combine(baseScriptDir, "c1001.lua"), "-- base");
        File.WriteAllText(Path.Combine(diyScriptDir, "c1001.lua"), "-- diy");

        ServerOptions options = new()
        {
            RootPath = tempDir,
            EnableNetworkListener = false,
            AutoLoadExpansions = true
        };

        IReadOnlyList<string> dbPaths = YGOProSharpServer.ResolveDatabasePaths(options);
        AssertEqual(3, dbPaths.Count);

        using ServerTestFakeOcgRuntime fakeRuntime = new();
        using YGOProSharpServerHost host = YGOProSharpServer.CreateHost(options, fakeRuntime);
        AssertTrue(fakeRuntime.LastOptions is not null);
        AssertTrue(fakeRuntime.LastOptions!.CardDataProvider.TryGetCardData(1001, out OcgCardData overridden));
        AssertEqual(2000, overridden.Attack);
        AssertTrue(fakeRuntime.LastOptions.CardDataProvider.TryGetCardData(9990010, out OcgCardData customCard));
        AssertEqual(3000, customCard.Attack);
        AssertTrue(fakeRuntime.LastOptions.ScriptProvider.TryGetScript("./script/c1001.lua", out byte[] scriptBytes));
        AssertEqual("-- diy", Encoding.UTF8.GetString(scriptBytes));
        AssertEqual("DiyLF", BanlistManager.Banlists[0].Name);

        // CLI 配置映射验证
        CliConfiguration config = CliConfiguration.Load(
        [
            "EnableMultiRoom=true",
            "MaxRooms=64",
            "AutoLoadExpansions=false",
            "ExpansionDirs=exp1;exp2",
            "ExtraDatabases=diy1.cdb,diy2.cdb",
            "ExtraScriptDirs=diy/script",
            "ExtraScripts=./script/global_diy.lua",
            "EnableChatCommands=false",
            "MatchWins=3"
        ]);
        ServerOptions cliOptions = CliOptionsFactory.CreateServerOptions(config);
        AssertTrue(cliOptions.EnableMultiRoom);
        AssertEqual(64, cliOptions.MaxRooms);
        AssertFalse(cliOptions.AutoLoadExpansions);
        AssertEqual(2, cliOptions.ExpansionDirectories.Count);
        AssertEqual(2, cliOptions.ExtraDatabases.Count);
        AssertEqual(1, cliOptions.ExtraScriptDirectories.Count);
        AssertEqual(1, cliOptions.Game.ExtraScripts.Count);
        AssertFalse(cliOptions.EnableChatCommands);
        AssertEqual(3, cliOptions.Game.MatchWins);
    }
    finally
    {
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }
}

static void CreateTestCardDb(
    string dbPath,
    IEnumerable<(int Id, int Ot, int Alias, long Setcode, int Type, int Level, int Race, int Attr, int Atk, int Def, string Name, string Desc)> cards)
{
    using SqliteConnection conn = new($"Data Source={dbPath}");
    conn.Open();
    using (SqliteCommand createCmd = conn.CreateCommand())
    {
        createCmd.CommandText = """
            CREATE TABLE datas (id INTEGER PRIMARY KEY, ot INTEGER, alias INTEGER, setcode INTEGER, type INTEGER, atk INTEGER, def INTEGER, level INTEGER, race INTEGER, attribute INTEGER, category INTEGER);
            CREATE TABLE texts (id INTEGER PRIMARY KEY, name TEXT, desc TEXT);
            """;
        createCmd.ExecuteNonQuery();
    }

    foreach (var c in cards)
    {
        using SqliteCommand insertData = conn.CreateCommand();
        insertData.CommandText = "INSERT INTO datas VALUES ($id,$ot,$alias,$setcode,$type,$atk,$def,$level,$race,$attr,0)";
        insertData.Parameters.AddWithValue("$id", c.Id);
        insertData.Parameters.AddWithValue("$ot", c.Ot);
        insertData.Parameters.AddWithValue("$alias", c.Alias);
        insertData.Parameters.AddWithValue("$setcode", c.Setcode);
        insertData.Parameters.AddWithValue("$type", c.Type);
        insertData.Parameters.AddWithValue("$atk", c.Atk);
        insertData.Parameters.AddWithValue("$def", c.Def);
        insertData.Parameters.AddWithValue("$level", c.Level);
        insertData.Parameters.AddWithValue("$race", c.Race);
        insertData.Parameters.AddWithValue("$attr", c.Attr);
        insertData.ExecuteNonQuery();

        using SqliteCommand insertText = conn.CreateCommand();
        insertText.CommandText = "INSERT INTO texts VALUES ($id,$name,$desc)";
        insertText.Parameters.AddWithValue("$id", c.Id);
        insertText.Parameters.AddWithValue("$name", c.Name);
        insertText.Parameters.AddWithValue("$desc", c.Desc);
        insertText.ExecuteNonQuery();
    }
}

static void SendPlayerJoin(YGOClient client, string name, string password)
{
    using (MemoryStream ms = new())
    using (BinaryWriter w = new(ms))
    {
        w.Write((byte)CtosMessage.PlayerInfo);
        w.WriteUnicode(name, 20);
        client.Send(ms.ToArray());
    }

    using (MemoryStream ms = new())
    using (BinaryWriter w = new(ms))
    {
        w.Write((byte)CtosMessage.JoinGame);
        w.Write((short)YGOProSharpServer.DefaultClientVersion);
        w.Write(0);
        w.Write((short)0);
        w.WriteUnicode(password, 20);
        client.Send(ms.ToArray());
    }
}

static void SendChat(YGOClient client, string text)
{
    using MemoryStream ms = new();
    using BinaryWriter w = new(ms);
    w.Write((byte)CtosMessage.Chat);
    w.WriteUnicode(text, text.Length + 1);
    client.Send(ms.ToArray());
}

static void SendSingleCardDeck(YGOClient client, int cardId)
{
    using MemoryStream ms = new();
    using BinaryWriter w = new(ms);
    w.Write((byte)CtosMessage.UpdateDeck);
    w.Write(1);
    w.Write(0);
    w.Write(cardId);
    client.Send(ms.ToArray());
}

static void SendSimpleCtos(YGOClient client, CtosMessage msg)
{
    client.Send([(byte)msg]);
}

static void SendByteCtos(YGOClient client, CtosMessage msg, byte value)
{
    client.Send([(byte)msg, value]);
}

static void PumpServerAndClients(CoreServer server, params YGOClient[] clients)
{
    for (int round = 0; round < 5; round++)
    {
        server.Tick();
        for (int i = 0; i < clients.Length; i++)
            clients[i].Update();
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

internal sealed class FakeLuaRuntime : ILuaRuntime
{
    private readonly Action<FakeLuaContext> _configure;

    public List<FakeLuaContext> CreatedContexts { get; } = new();

    public FakeLuaRuntime(Action<FakeLuaContext> configure)
    {
        _configure = configure;
    }

    public ILuaScriptContext CreateContext(string? name = null)
    {
        FakeLuaContext context = new(name, _configure);
        CreatedContexts.Add(context);
        return context;
    }
}

internal sealed class FakeLuaContext : ILuaScriptContext
{
    private readonly Action<FakeLuaContext> _configure;

    public string? Name { get; }
    public Dictionary<string, object?> Globals { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FakeLuaFunction> Functions { get; } = new(StringComparer.Ordinal);
    public List<FakeLuaFunction> TrackedFunctions { get; } = new();
    public object? ModuleResult { get; set; }
    public bool Disposed { get; private set; }

    public FakeLuaContext(string? name, Action<FakeLuaContext> configure)
    {
        Name = name;
        _configure = configure;
    }

    public void SetGlobal(string name, object? value) => Globals[name] = value;

    public bool TryGetGlobal(string name, out object? value) => Globals.TryGetValue(name, out value);

    public object? DoString(string code, string? chunkName = null)
    {
        _configure(this);
        return ModuleResult;
    }

    public bool TryGetFunction(string name, out ILuaFunction? function)
    {
        if (Functions.TryGetValue(name, out FakeLuaFunction? found))
        {
            TrackedFunctions.Add(found);
            function = found;
            return true;
        }

        function = null;
        return false;
    }

    public bool TryWrapFunction(object rawFunction, out ILuaFunction? function)
    {
        if (rawFunction is FakeRawLuaClosure closure)
        {
            FakeLuaFunction wrapped = new(closure.Callback);
            TrackedFunctions.Add(wrapped);
            function = wrapped;
            return true;
        }

        function = null;
        return false;
    }

    public bool TryConvertList<T>(object? value, out IList<T>? list)
    {
        if (value is FakeLuaCardArray cardArray && typeof(T) == typeof(WindBot.Game.ClientCard))
        {
            list = (IList<T>)(object)new List<WindBot.Game.ClientCard>(cardArray.Cards);
            return true;
        }

        list = null;
        return false;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeLuaTable : ILuaTable
{
    public Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FakeLuaFunction> Functions { get; } = new(StringComparer.Ordinal);
    public bool Disposed { get; private set; }

    public bool TryGet(string key, out object? value) => Fields.TryGetValue(key, out value);

    public bool TryGetFunction(string key, out ILuaFunction? function)
    {
        if (Functions.TryGetValue(key, out FakeLuaFunction? found))
        {
            function = found;
            return true;
        }

        function = null;
        return false;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakeLuaFunction : ILuaFunction
{
    private readonly Func<object?[], object?> _handler;

    public bool Disposed { get; private set; }

    public FakeLuaFunction(Func<object?[], object?> handler)
    {
        _handler = handler;
    }

    public object? Call(params object?[] args) => _handler(args);

    public void Dispose() => Disposed = true;
}

internal sealed record FakeRawLuaClosure(Func<object?[], object?> Callback);

internal sealed record FakeLuaCardArray(IReadOnlyList<WindBot.Game.ClientCard> Cards);

internal sealed class ServerTestDelegateDeckValidator(Func<DeckValidationContext, int> validator) : IDeckValidator
{
    public int Validate(DeckValidationContext context) => validator(context);
}

internal sealed class ServerTestRecordingAddon : AddonBase
{
    private readonly Action _onDispose;
    private readonly Action<DuelStartContext> _onBeforeDuelStart;

    public ServerTestRecordingAddon(Game game, Action onInit, Action onDispose, Action<DuelStartContext> onBeforeDuelStart)
        : base(game)
    {
        _onDispose = onDispose;
        _onBeforeDuelStart = onBeforeDuelStart;
        Game.OnBeforeDuelStart += HandleBeforeDuelStart;
        onInit();
    }

    private void HandleBeforeDuelStart(object sender, DuelStartContext e) => _onBeforeDuelStart(e);

    public override void Dispose()
    {
        Game.OnBeforeDuelStart -= HandleBeforeDuelStart;
        _onDispose();
    }
}

internal sealed class ServerTestFakeDuelFactory : IDuelFactory
{
    public ServerTestFakeDuelSession? LastSession { get; private set; }
    public IDuelSession Create(uint seed) => LastSession = new ServerTestFakeDuelSession();
    public IDuelSession Create(ReadOnlySpan<uint> seedSequence) => LastSession = new ServerTestFakeDuelSession();
    public IDuelSession CreateLegacy(uint seed) => LastSession = new ServerTestFakeDuelSession();
}

internal sealed class ServerTestFakeDuelSession : IDuelSession
{
    public List<string> PreloadedScripts { get; } = new();
    public List<(int CardId, int Owner, CardLocation Location)> AddedCards { get; } = new();
    public bool Started { get; private set; }

    public void SetAnalyzer(DuelMessageAnalyzer analyzer) { }
    public void SetErrorHandler(Action<string> errorHandler) { }
    public void InitPlayers(int startLp, int startHand, int drawCount) { }
    public void AddCard(int cardId, int owner, CardLocation location) => AddedCards.Add((cardId, owner, location));
    public void AddTagCard(int cardId, int owner, CardLocation location) => AddedCards.Add((cardId, owner, location));
    public void Start(int options) => Started = true;
    public int Process() => 0;
    public void SetResponse(int response) { }
    public void SetResponse(ReadOnlySpan<byte> response) { }
    public int QueryFieldCount(int player, CardLocation location) => AddedCards.Count(c => c.Owner == player && c.Location == location);
    public int QueryFieldCard(int player, CardLocation location, Span<byte> destination, int flag = 16777215, bool useCache = false) => 0;
    public int QueryCard(int player, int location, int sequence, Span<byte> destination, int flag = 16777215, bool useCache = false) => 0;
    public int QueryFieldInfo(Span<byte> destination) => 1;
    public int PreloadScript(string scriptName) { PreloadedScripts.Add(scriptName); return 1; }
    public void End() { }
    public void Dispose() { }
}

internal sealed class ServerTestFakeOcgRuntime : IOcgRuntime
{
    public OcgRuntimeOptions? LastOptions { get; private set; }
    public IDuelFactory DuelFactory { get; } = new ServerTestFakeDuelFactory();
    public void Initialize(OcgRuntimeOptions options) => LastOptions = options;
    public void Dispose() { }
}
