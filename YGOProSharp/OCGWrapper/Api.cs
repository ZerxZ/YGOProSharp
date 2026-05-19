using System.Runtime.InteropServices;

namespace YGOProSharp.OCGWrapper;

public static unsafe class Api
{
    private const nuint ScriptBufferSize = 1024 * 128;

    private static readonly object SyncRoot = new();

    private static string _rootPath = ".";
    private static string _scriptDirectory = "script";
    private static byte* _scriptBuffer;

    private static ScriptReader? _scriptCallback;
    private static CardReader? _cardCallback;
    private static MessageHandler? _messageCallback;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ScriptReader(string scriptName, int* length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint CardReader(uint code, Card.CardData* data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint MessageHandler(IntPtr duel, uint messageType);

    public static void Init(string rootPath = ".", string scriptDirectory = "script", string databaseFile = "cards.cdb")
    {
        lock (SyncRoot)
        {
            DisposeCore();

            _rootPath = rootPath;
            _scriptDirectory = scriptDirectory;

            CardsManager.Init(Path.Combine(Path.GetFullPath(rootPath), databaseFile));
            Duel.Duels = new Dictionary<IntPtr, Duel>();

            _scriptBuffer = (byte*)NativeMemory.Alloc(ScriptBufferSize);

            _cardCallback = OnCardReader;
            _scriptCallback = OnScriptReader;
            _messageCallback = OnMessageHandler;

            OcgCoreNative.SetCardReader(_cardCallback);
            OcgCoreNative.SetScriptReader(_scriptCallback);
            OcgCoreNative.SetMessageHandler(_messageCallback);
        }
    }

    public static void Dispose()
    {
        lock (SyncRoot)
        {
            DisposeCore();
        }
    }

    private static void DisposeCore()
    {
        foreach (Duel duel in Duel.Duels.Values.ToArray())
            duel.Dispose();

        Duel.Duels.Clear();

        if (_scriptBuffer is not null)
        {
            NativeMemory.Free(_scriptBuffer);
            _scriptBuffer = null;
        }

        _scriptCallback = null;
        _cardCallback = null;
        _messageCallback = null;
    }

    private static uint OnCardReader(uint code, Card.CardData* data)
    {
        Card? card = CardsManager.GetCard((int)code);
        if (card is not null)
            *data = card.Data;

        return code;
    }

    private static IntPtr OnScriptReader(string scriptName, int* length)
    {
        string filename = GetScriptFilename(scriptName);
        if (!File.Exists(filename) || _scriptBuffer is null)
            return IntPtr.Zero;

        byte[] content = File.ReadAllBytes(filename);
        if ((nuint)content.Length > ScriptBufferSize)
            return IntPtr.Zero;

        content.AsSpan().CopyTo(new Span<byte>(_scriptBuffer, content.Length));
        *length = content.Length;

        return (IntPtr)_scriptBuffer;
    }

    private static uint OnMessageHandler(IntPtr duelPtr, uint messageType)
    {
        if (Duel.Duels.TryGetValue(duelPtr, out Duel? duel))
            duel.OnMessage(messageType);

        return 0;
    }

    private static string GetScriptFilename(string scriptName)
    {
        return Path.Combine(_rootPath, scriptName.Replace("./script", _scriptDirectory));
    }
}
