using System.Runtime.InteropServices;
using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.NativeApi;

/// <summary>
/// ocgcore 的托管 runtime 边界（managed runtime boundary），负责 native callback、共享 script buffer 生命周期和活动 duel session。
/// </summary>
public sealed unsafe class NativeOcgRuntime : IOcgRuntime
{
    private const nuint ScriptBufferSize = 0x100000;

    private readonly object _syncRoot = new();
    private readonly Dictionary<IntPtr, NativeDuelSession> _duels = new();
    private readonly NativeDuelFactory _duelFactory;

    private OcgRuntimeOptions? _options;
    private IntPtr _scriptBuffer;
    private OcgCoreImports.ScriptReader? _scriptReader;
    private OcgCoreImports.CardReader? _cardReader;
    private OcgCoreImports.MessageHandler? _messageHandler;

    public NativeOcgRuntime()
    {
        _duelFactory = new NativeDuelFactory(this);
    }

    public IDuelFactory DuelFactory => _duelFactory;

    internal bool IsInitialized => _options is not null;

    /// <summary>
    /// 向 ocgcore 注册托管 provider，并为后续 duel session 准备 callback 状态。
    /// </summary>
    public void Initialize(OcgRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (_syncRoot)
        {
            DisposeCore();

            _options = options;
            _scriptBuffer = (IntPtr)NativeMemory.Alloc(ScriptBufferSize);

            _scriptReader = OnScriptReader;
            _cardReader = OnCardReader;
            _messageHandler = OnMessageHandler;

            try
            {
                OcgCoreImports.SetCardReader(_cardReader);
                OcgCoreImports.SetScriptReader(_scriptReader);
                OcgCoreImports.SetMessageHandler(_messageHandler);
            }
            catch (Exception exception) when (NativeOcgErrors.IsNativeRuntimeFailure(exception))
            {
                DisposeCore();
                throw NativeOcgErrors.CreateRuntimeFailure(exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
            DisposeCore();
    }

    internal void Register(NativeDuelSession session)
    {
        lock (_syncRoot)
            _duels.Add(session.NativeHandle, session);
    }

    internal void Unregister(IntPtr nativeHandle)
    {
        lock (_syncRoot)
            _duels.Remove(nativeHandle);
    }

    internal void EnsureInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("Native OCG runtime has not been initialized. Reference YGOProSharp.Native and call Initialize before creating duels.");
    }

    private void DisposeCore()
    {
        foreach (NativeDuelSession duel in _duels.Values.ToArray())
            duel.Dispose();

        _duels.Clear();

        if (_scriptBuffer != IntPtr.Zero)
        {
            NativeMemory.Free((void*)_scriptBuffer);
            _scriptBuffer = IntPtr.Zero;
        }

        _options = null;
        _scriptReader = null;
        _cardReader = null;
        _messageHandler = null;
    }

    private uint OnCardReader(uint code, OcgCardData* data)
    {
        // native core 按 code 请求 card_data；存储策略仍留在已配置的 provider 中。
        if (_options?.CardDataProvider.TryGetCardData(code, out OcgCardData cardData) == true)
            *data = cardData;

        return code;
    }

    private IntPtr OnScriptReader(string scriptName, int* length)
    {
        if (_scriptBuffer == IntPtr.Zero || _options is null)
            return IntPtr.Zero;

        if (!_options.ScriptProvider.TryGetScript(scriptName, out byte[] script))
            return IntPtr.Zero;

        if ((nuint)script.Length > ScriptBufferSize)
            return IntPtr.Zero;

        script.AsSpan().CopyTo(new Span<byte>((void*)_scriptBuffer, script.Length));
        *length = script.Length;
        return _scriptBuffer;
    }

    private uint OnMessageHandler(IntPtr duelPtr, uint messageType)
    {
        NativeDuelSession? duel;
        lock (_syncRoot)
            _duels.TryGetValue(duelPtr, out duel);

        duel?.OnMessage(messageType);
        return 0;
    }
}
