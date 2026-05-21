using System.Buffers;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.Abstractions.Ocg;

public delegate int DuelMessageAnalyzer(GameMessage message, ref SequenceReader<byte> reader, ReadOnlyMemory<byte> raw);

/// <summary>
/// 进程级 OCG runtime 抽象（process-level runtime），实现负责配置 callback 并暴露 duel session 工厂。
/// </summary>
public interface IOcgRuntime : IDisposable
{
    IDuelFactory DuelFactory { get; }

    void Initialize(OcgRuntimeOptions options);
}

/// <summary>
/// 创建托管 duel session，并隐藏底层 native 路径是 legacy seed 还是 seed sequence。
/// </summary>
public interface IDuelFactory
{
    IDuelSession Create(uint seed);

    IDuelSession Create(ReadOnlySpan<uint> seedSequence);

    IDuelSession CreateLegacy(uint seed);
}

/// <summary>
/// 单个 duel 的托管操作面（managed operations），调用方不需要 native handle、pointer 或 ocgapi buffer。
/// </summary>
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

/// <summary>
/// 从服务层传入 native interop 层的 runtime 配置。
/// </summary>
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

/// <summary>
/// 向 ocgcore callback 提供 card_data 记录。
/// </summary>
public interface ICardDataProvider
{
    bool TryGetCardData(uint code, out OcgCardData data);
}

/// <summary>
/// 向 ocgcore callback 提供 Lua script bytes。
/// </summary>
public interface IScriptProvider
{
    bool TryGetScript(string scriptName, out byte[] script);
}
