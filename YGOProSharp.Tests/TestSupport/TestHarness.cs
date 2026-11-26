using System.Runtime.CompilerServices;

namespace YGOProSharp.Tests.TestSupport;

/// <summary>
/// 测试公共断言桥接（底层委托给 xUnit <see cref="Assert"/>）与共享夹具构建器。
/// </summary>
internal static class TestHarness
{
    private static ILoggerFactory? _loggerFactory;

    [ModuleInitializer]
    internal static void InitializeLogging()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(TestLog.Provider);
        });
        AppLog.Configure(_loggerFactory);
    }

    internal static void AssertEqual<T>(T expected, T actual)
        where T : IEquatable<T>
        => Assert.Equal(expected, actual);

    internal static void AssertFalse(bool value)
        => Assert.False(value);

    internal static void AssertTrue(bool value)
        => Assert.True(value);

    internal static void AssertSequenceEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        => Assert.Equal(expected.ToArray(), actual.ToArray());

    internal static void AssertCollectionEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        where T : IEquatable<T>
        => Assert.Equal(expected.ToArray(), actual.ToArray());

    internal static void AssertThrows<TException>(Action action)
        where TException : Exception
        => Assert.Throws<TException>(action);

    internal static int GetAvailableTcpPort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    internal static async Task VerifyYgoClientWebSocketModeAsync(WebSocketFramingMode mode)
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

    internal static async Task<T> WaitAsync<T>(Task<T> task)
    {
        Task completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != task)
            throw new TimeoutException("Timed out waiting for asynchronous test operation.");

        return await task;
    }

    internal static Card TestCard(
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

    internal static InMemoryCardRepository TestRepository(params Card[] cards)
    {
        return new InMemoryCardRepository(cards);
    }

    internal static NamedCard TestNamedCard(
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

    internal static InMemoryNamedCardRepository TestNamedRepository(params NamedCard[] cards)
    {
        return new InMemoryNamedCardRepository(cards);
    }

    internal static TestCdbRow TestCdbRow(int id, int type, int levelInfo, int defense, string name)
    {
        return new TestCdbRow(id, type, levelInfo, defense, name);
    }

    internal static void CreateTestCdb(string path, IReadOnlyList<TestCdbRow> rows)
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

    internal static string FindRepositoryRoot()
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

    internal static string ProjectText(string projectDirectory)
    {
        IEnumerable<string> files = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Where(IsRepositorySourcePath);

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    internal static bool IsRepositorySourcePath(string file)
    {
        string fullPath = Path.GetFullPath(file);
        string separator = Path.DirectorySeparatorChar.ToString();
        string normalized = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !normalized.Contains(separator + "bin" + separator, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(separator + "obj" + separator, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(separator + ".external" + separator, StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains(separator + ".git" + separator, StringComparison.OrdinalIgnoreCase);
    }

    internal static void AssertDoesNotContainAny(string text, IEnumerable<string> forbiddenTokens, string subject)
    {
        foreach (string token in forbiddenTokens)
        {
            if (text.Contains(token, StringComparison.Ordinal))
                throw new InvalidOperationException($"{subject} contains forbidden dependency token {token}.");
        }
    }

    internal static List<string> CaptureDialogChatMessages(
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

    internal static void CreateTestCardDb(
        string dbPath,
        IEnumerable<(int Id, int Ot, int Alias, long Setcode, int Type, int Level, int Race, int Attr, int Atk, int Def, string Name, string Desc)> cards)
    {
        using SqliteConnection conn = new($"Data Source={dbPath};Pooling=False");
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

    internal static void SendPlayerJoin(YGOClient client, string name, string password)
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

    internal static void SendChat(YGOClient client, string text)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write((byte)CtosMessage.Chat);
        w.WriteUnicode(text, text.Length + 1);
        client.Send(ms.ToArray());
    }

    internal static void SendSingleCardDeck(YGOClient client, int cardId)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write((byte)CtosMessage.UpdateDeck);
        w.Write(1);
        w.Write(0);
        w.Write(cardId);
        client.Send(ms.ToArray());
    }

    internal static void SendSimpleCtos(YGOClient client, CtosMessage msg)
    {
        client.Send([(byte)msg]);
    }

    internal static void SendByteCtos(YGOClient client, CtosMessage msg, byte value)
    {
        client.Send([(byte)msg, value]);
    }

    internal static void PumpServerAndClients(CoreServer server, params YGOClient[] clients)
    {
        for (int round = 0; round < 5; round++)
        {
            server.Tick();
            for (int i = 0; i < clients.Length; i++)
                clients[i].Update();
        }
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
