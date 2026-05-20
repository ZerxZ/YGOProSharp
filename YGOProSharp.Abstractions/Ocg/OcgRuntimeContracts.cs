using System.Buffers;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Abstractions.Ocg;

public delegate int DuelMessageAnalyzer(GameMessage message, ref SequenceReader<byte> reader, ReadOnlyMemory<byte> raw);

public interface IOcgRuntime : IDisposable
{
    IDuelFactory DuelFactory { get; }

    void Initialize(OcgRuntimeOptions options);
}

public interface IDuelFactory
{
    IDuelSession Create(uint seed);

    IDuelSession Create(ReadOnlySpan<uint> seedSequence);

    IDuelSession CreateLegacy(uint seed);
}

public interface IDuelSession : IDisposable
{
    void SetAnalyzer(DuelMessageAnalyzer analyzer);

    void SetErrorHandler(Action<string> errorHandler);

    void InitPlayers(int startLp, int startHand, int drawCount);

    void AddCard(int cardId, int owner, CardLocation location);

    void AddTagCard(int cardId, int owner, CardLocation location);

    void Start(int options);

    int Process();

    void SetResponse(int response);

    void SetResponse(ReadOnlySpan<byte> response);

    int QueryFieldCount(int player, CardLocation location);

    int QueryFieldCard(int player, CardLocation location, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false);

    int QueryCard(int player, int location, int sequence, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false);

    int QueryFieldInfo(Span<byte> destination);

    int PreloadScript(string scriptName);

    void End();
}

public sealed class OcgRuntimeOptions
{
    public OcgRuntimeOptions(
        string rootPath,
        string scriptDirectory,
        string databaseFile,
        ICardDataProvider cardDataProvider,
        IScriptProvider scriptProvider)
    {
        RootPath = rootPath;
        ScriptDirectory = scriptDirectory;
        DatabaseFile = databaseFile;
        CardDataProvider = cardDataProvider;
        ScriptProvider = scriptProvider;
    }

    public string RootPath { get; }

    public string ScriptDirectory { get; }

    public string DatabaseFile { get; }

    public ICardDataProvider CardDataProvider { get; }

    public IScriptProvider ScriptProvider { get; }
}

public interface ICardDataProvider
{
    bool TryGetCardData(uint code, out OcgCardData data);
}

public interface IScriptProvider
{
    bool TryGetScript(string scriptName, out byte[] script);
}
