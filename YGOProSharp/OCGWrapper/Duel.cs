using System.Buffers;
using System.Text;
using YGOProSharp.OCGWrapper.Enums;

namespace YGOProSharp.OCGWrapper;

public delegate int DuelMessageAnalyzer(GameMessage message, ref SequenceReader<byte> reader, ReadOnlyMemory<byte> raw);

public sealed class Duel : IDisposable
{
    public const int MaxNativeMessageLength = 4096;
    public const int MaxQueryResultLength = 4096;
    public const int FieldInfoLength = 256;
    public const int MaxResponseLength = 64;

    private readonly DuelHandle _handle;
    private readonly byte[] _messageBuffer = new byte[MaxNativeMessageLength];
    private readonly byte[] _queryBuffer = new byte[MaxQueryResultLength];
    private readonly byte[] _logBuffer = new byte[FieldInfoLength];
    private readonly byte[] _responseBuffer = new byte[MaxResponseLength];

    private DuelMessageAnalyzer? _analyzer;
    private Action<string>? _errorHandler;
    private bool _disposed;

    internal static IDictionary<IntPtr, Duel> Duels = new Dictionary<IntPtr, Duel>();

    internal Duel(DuelHandle handle)
    {
        _handle = handle;
        Duels.Add(_handle.DangerousGetHandle(), this);
    }

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
        OcgCoreNative.SetPlayerInfo(_handle, 0, startLp, startHand, drawCount);
        OcgCoreNative.SetPlayerInfo(_handle, 1, startLp, startHand, drawCount);
    }

    public void AddCard(int cardId, int owner, CardLocation location)
    {
        ThrowIfDisposed();
        OcgCoreNative.NewCard(_handle, (uint)cardId, (byte)owner, (byte)owner, (byte)location, 0, (byte)CardPosition.FaceDownDefence);
    }

    public void AddTagCard(int cardId, int owner, CardLocation location)
    {
        ThrowIfDisposed();
        OcgCoreNative.NewTagCard(_handle, (uint)cardId, (byte)owner, (byte)location);
    }

    public void Start(int options)
    {
        ThrowIfDisposed();
        OcgCoreNative.StartDuel(_handle, options);
    }

    public unsafe int Process()
    {
        ThrowIfDisposed();

        int fail = 0;
        while (true)
        {
            int processResult = OcgCoreNative.Process(_handle);
            int length = processResult & 0xFFFF;

            if (length > 0)
            {
                if (length > _messageBuffer.Length)
                    throw new InvalidOperationException($"Native message length {length} exceeds buffer size {_messageBuffer.Length}.");

                fail = 0;
                fixed (byte* buffer = _messageBuffer)
                    OcgCoreNative.GetMessage(_handle, buffer);

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
        OcgCoreNative.SetResponseInt(_handle, (uint)response);
    }

    public unsafe void SetResponse(ReadOnlySpan<byte> response)
    {
        ThrowIfDisposed();
        if (response.Length > MaxResponseLength)
            return;

        if (!TryCopyResponse(response, _responseBuffer))
            return;

        fixed (byte* buffer = _responseBuffer)
            OcgCoreNative.SetResponseBytes(_handle, buffer);
    }

    public int QueryFieldCount(int player, CardLocation location)
    {
        ThrowIfDisposed();
        return OcgCoreNative.QueryFieldCount(_handle, (byte)player, (byte)location);
    }

    public unsafe int QueryFieldCard(int player, CardLocation location, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false)
    {
        ThrowIfDisposed();

        int length;
        fixed (byte* buffer = _queryBuffer)
            length = OcgCoreNative.QueryFieldCard(_handle, (byte)player, (byte)location, flag, buffer, useCache ? 1 : 0);

        CopyQueryResult(_queryBuffer, length, destination, nameof(QueryFieldCard));
        return length;
    }

    public unsafe int QueryCard(int player, int location, int sequence, Span<byte> destination, int flag = 0xFFFFFF & ~(int)Query.ReasonCard, bool useCache = false)
    {
        ThrowIfDisposed();

        int length;
        fixed (byte* buffer = _queryBuffer)
            length = OcgCoreNative.QueryCard(_handle, (byte)player, (byte)location, (byte)sequence, flag, buffer, useCache ? 1 : 0);

        CopyQueryResult(_queryBuffer, length, destination, nameof(QueryCard));
        return length;
    }

    public unsafe int QueryFieldInfo(Span<byte> destination)
    {
        ThrowIfDisposed();
        if (destination.Length < FieldInfoLength)
            throw new ArgumentException($"Destination buffer must be at least {FieldInfoLength} bytes.", nameof(destination));

        fixed (byte* buffer = _queryBuffer)
            OcgCoreNative.QueryFieldInfo(_handle, buffer);

        _queryBuffer.AsSpan(0, FieldInfoLength).CopyTo(destination);
        return FieldInfoLength;
    }

    public void End()
    {
        Dispose();
    }

    public IntPtr GetNativePtr()
    {
        return _handle.DangerousGetHandle();
    }

    internal void OnMessage(uint messageType)
    {
        ThrowIfDisposed();

        unsafe
        {
            fixed (byte* buffer = _logBuffer)
                OcgCoreNative.GetLogMessage(_handle, buffer);
        }

        ReadOnlySpan<byte> messageBytes = _logBuffer;
        int terminator = messageBytes.IndexOf((byte)0);
        if (terminator >= 0)
            messageBytes = messageBytes[..terminator];

        _errorHandler?.Invoke(Encoding.UTF8.GetString(messageBytes));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Duels.Remove(_handle.DangerousGetHandle());
        _handle.Dispose();
    }

    public static Duel Create(uint seed)
    {
        MtRandom random = new();
        random.Reset(seed);

        IntPtr nativeHandle = OcgCoreNative.CreateDuel(random.Rand());
        return Create(nativeHandle) ?? throw new InvalidOperationException("Could not create native duel.");
    }

    internal static Duel? Create(IntPtr nativeHandle)
    {
        return nativeHandle == IntPtr.Zero ? null : new Duel(new DuelHandle(nativeHandle));
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

    internal static bool TryCopyResponse(ReadOnlySpan<byte> response, Span<byte> destination)
    {
        if (response.Length > MaxResponseLength)
            return false;

        if (destination.Length < MaxResponseLength)
            throw new ArgumentException($"Destination buffer must be at least {MaxResponseLength} bytes.", nameof(destination));

        destination[..MaxResponseLength].Clear();
        response.CopyTo(destination);
        return true;
    }

    internal static void CopyQueryResult(byte[] source, int length, Span<byte> destination, string caller)
    {
        if (length < 0)
            throw new InvalidOperationException($"{caller} returned invalid length {length}.");

        if (length > source.Length)
            throw new InvalidOperationException($"{caller} returned length {length}, exceeding internal buffer size {source.Length}.");

        if (destination.Length < length)
            throw new ArgumentException($"Destination buffer must be at least {length} bytes.", nameof(destination));

        source.AsSpan(0, length).CopyTo(destination);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
