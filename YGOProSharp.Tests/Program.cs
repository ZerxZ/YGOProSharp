using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
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
Run("Project boundaries keep direct Console writes out of source", ProjectBoundariesKeepDirectConsoleWritesOutOfSource);
Run("Project boundaries keep logger parameters out of business APIs", ProjectBoundariesKeepLoggerParametersOutOfBusinessApis);
Run("PacketFramer handles split and sticky packets", PacketFramerHandlesSplitAndStickyPackets);
Run("PacketFramer handles 4-byte size-included headers", PacketFramerHandlesFourByteSizeIncludedHeaders);
Run("PacketFramer rejects oversize packets", PacketFramerRejectsOversizePackets);
Run("PacketReader reads little-endian and UTF-16 data", PacketReaderReadsLittleEndianAndUtf16Data);
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
                 .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
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
                 .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
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
                 .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
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
                 .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
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
                 .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
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
        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                       !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
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
