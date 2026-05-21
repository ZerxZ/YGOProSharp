using System.Buffers;
using System.Text;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp.NativeApi;

/// <summary>
/// 单个 native duel handle 的安全托管门面（managed facade），负责 message/query/response buffer 并转换 raw ocgapi 调用。
/// </summary>
public sealed class NativeDuelSession : IDuelSession
{
    private readonly NativeOcgRuntime _runtime;
    private readonly DuelHandle _handle;
    private readonly byte[] _messageBuffer = new byte[OcgCoreConstants.MaxNativeMessageLength];
    private readonly byte[] _queryBuffer = new byte[OcgCoreConstants.MaxQueryResultLength];
    private readonly byte[] _logBuffer = new byte[OcgCoreConstants.FieldInfoLength];
    private readonly byte[] _responseBuffer = new byte[OcgCoreConstants.MaxResponseLength];

    private DuelMessageAnalyzer? _analyzer;
    private Action<string>? _errorHandler;
    private bool _disposed;

    internal NativeDuelSession(NativeOcgRuntime runtime, DuelHandle handle)
    {
        _runtime = runtime;
        _handle = handle;
    }

    internal IntPtr NativeHandle => _handle.DangerousGetHandle();

    public void SetAnalyzer(DuelMessageAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    public void SetErrorHandler(Action<string> errorHandler)
    {
        _errorHandler = errorHandler;
    }

    public void InitPlayers(int startLp, int startHand, int drawCount)
    {
        ThrowIfDisposed();
        OcgCoreImports.SetPlayerInfo(_handle, 0, startLp, startHand, drawCount);
        OcgCoreImports.SetPlayerInfo(_handle, 1, startLp, startHand, drawCount);
    }

    public void AddCard(int cardId, int owner, CardLocation location)
    {
        ThrowIfDisposed();
        OcgCoreImports.NewCard(_handle, (uint)cardId, (byte)owner, (byte)owner, (byte)location, 0, (byte)CardPosition.FaceDownDefence);
    }

    public void AddTagCard(int cardId, int owner, CardLocation location)
    {
        ThrowIfDisposed();
        OcgCoreImports.NewTagCard(_handle, (uint)cardId, (byte)owner, (byte)location);
    }

    public void Start(int options)
    {
        ThrowIfDisposed();
        OcgCoreImports.StartDuel(_handle, (uint)options);
    }

    public unsafe int Process()
    {
        ThrowIfDisposed();

        int fail = 0;
        while (true)
        {
            uint processResult = OcgCoreImports.Process(_handle);
            int length = (int)(processResult & 0xFFFF);

            if (length > 0)
            {
                if (length > _messageBuffer.Length)
                    throw new InvalidOperationException($"Native message length {length} exceeds buffer size {_messageBuffer.Length}.");

                fail = 0;
                fixed (byte* buffer = _messageBuffer)
                    OcgCoreImports.GetMessage(_handle, buffer);

                // message 解释交回核心层 GameAnalyser；本类只管理 native buffer。
                int result = HandleMessages(_messageBuffer.AsMemory(0, length));
                if (result != 0)
                    return result;
            }
            else if (++fail == 10)
            {
                return -1;
            }
        }
    }

    public void SetResponse(int response)
    {
        ThrowIfDisposed();
        OcgCoreImports.SetResponseInt(_handle, response);
    }

    public unsafe void SetResponse(ReadOnlySpan<byte> response)
    {
        ThrowIfDisposed();
        if (!TryCopyResponse(response, _responseBuffer))
            return;

        fixed (byte* buffer = _responseBuffer)
            OcgCoreImports.SetResponseBytes(_handle, buffer);
    }

    public int QueryFieldCount(int player, CardLocation location)
    {
        ThrowIfDisposed();
        return OcgCoreImports.QueryFieldCount(_handle, (byte)player, (byte)location);
    }

    public unsafe int QueryFieldCard(int player, CardLocation location, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false)
    {
        ThrowIfDisposed();

        int length;
        fixed (byte* buffer = _queryBuffer)
            length = OcgCoreImports.QueryFieldCard(_handle, (byte)player, (byte)location, (uint)flag, buffer, useCache ? 1 : 0);

        CopyQueryResult(_queryBuffer, length, destination, nameof(QueryFieldCard));
        return length;
    }

    public unsafe int QueryCard(int player, int location, int sequence, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false)
    {
        ThrowIfDisposed();

        int length;
        fixed (byte* buffer = _queryBuffer)
            length = OcgCoreImports.QueryCard(_handle, (byte)player, (byte)location, (byte)sequence, (uint)flag, buffer, useCache ? 1 : 0);

        CopyQueryResult(_queryBuffer, length, destination, nameof(QueryCard));
        return length;
    }

    public unsafe int QueryFieldInfo(Span<byte> destination)
    {
        ThrowIfDisposed();
        if (destination.Length < OcgCoreConstants.FieldInfoLength)
            throw new ArgumentException($"Destination buffer must be at least {OcgCoreConstants.FieldInfoLength} bytes.", nameof(destination));

        fixed (byte* buffer = _queryBuffer)
            OcgCoreImports.QueryFieldInfo(_handle, buffer);

        _queryBuffer.AsSpan(0, OcgCoreConstants.FieldInfoLength).CopyTo(destination);
        return OcgCoreConstants.FieldInfoLength;
    }

    public int PreloadScript(string scriptName)
    {
        ThrowIfDisposed();
        try
        {
            return OcgCoreImports.PreloadScript(_handle, scriptName);
        }
        catch (Exception exception) when (NativeOcgErrors.IsNativeRuntimeFailure(exception))
        {
            throw NativeOcgErrors.CreateRuntimeFailure(exception);
        }
    }

    public void End()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // 释放 handle 前先 unregister，避免滞后的 native callback 解析到已释放 session。
        _runtime.Unregister(NativeHandle);
        _handle.Dispose();
    }

    internal unsafe void OnMessage(uint messageType)
    {
        ThrowIfDisposed();

        fixed (byte* buffer = _logBuffer)
            OcgCoreImports.GetLogMessage(_handle, buffer);

        ReadOnlySpan<byte> messageBytes = _logBuffer;
        int terminator = messageBytes.IndexOf((byte)0);
        if (terminator >= 0)
            messageBytes = messageBytes[..terminator];

        _errorHandler?.Invoke(Encoding.UTF8.GetString(messageBytes));
    }

    public static bool TryCopyResponse(ReadOnlySpan<byte> response, Span<byte> destination)
    {
        if (response.Length > OcgCoreConstants.MaxResponseLength)
            return false;

        if (destination.Length < OcgCoreConstants.MaxResponseLength)
            throw new ArgumentException($"Destination buffer must be at least {OcgCoreConstants.MaxResponseLength} bytes.", nameof(destination));

        destination[..OcgCoreConstants.MaxResponseLength].Clear();
        response.CopyTo(destination);
        return true;
    }

    public static void CopyQueryResult(byte[] source, int length, Span<byte> destination, string caller)
    {
        if (length < 0)
            throw new InvalidOperationException($"{caller} returned invalid length {length}.");

        if (length > source.Length)
            throw new InvalidOperationException($"{caller} returned length {length}, exceeding internal buffer size {source.Length}.");

        if (destination.Length < length)
            throw new ArgumentException($"Destination buffer must be at least {length} bytes.", nameof(destination));

        source.AsSpan(0, length).CopyTo(destination);
    }

    private int HandleMessages(ReadOnlyMemory<byte> raw)
    {
        SequenceReader<byte> reader = new(new ReadOnlySequence<byte>(raw));

        while (!reader.End)
        {
            if (!reader.TryRead(out byte messageId))
                throw new EndOfStreamException("Native message ended before the message id could be read.");

            GameMessage message = (GameMessage)messageId;
            ReadOnlyMemory<byte> body = raw[(int)reader.Consumed..];

            int result = _analyzer?.Invoke(message, ref reader, body) ?? -1;
            if (result != 0)
                return result;
        }

        return 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
