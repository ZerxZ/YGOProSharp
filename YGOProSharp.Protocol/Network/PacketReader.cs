using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace YGOProSharp.Protocol;

public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _buffer;

    public PacketReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }

    public int Position { get; private set; }

    public int Remaining => _buffer.Length - Position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return _buffer[Position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadSpan(int length)
    {
        EnsureAvailable(length);
        ReadOnlySpan<byte> span = _buffer.Slice(Position, length);
        Position += length;
        return span;
    }

    public string ReadUnicode(int length)
    {
        ReadOnlySpan<byte> bytes = ReadSpan(length * sizeof(char));
        string text = Encoding.Unicode.GetString(bytes);
        int terminator = text.IndexOf('\0');
        return terminator >= 0 ? text[..terminator] : text;
    }

    public string ReadUnicodeAutoLength(int maxLength)
    {
        if (maxLength < 0)
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, "Max length must be non-negative.");

        // 变长 UTF-16 消息（如 CTOS_CHAT）只携带实际字符与结尾空字符，按剩余字节上限读取。
        int maxBytes = Math.Min(Remaining, maxLength * sizeof(char));
        int evenBytes = maxBytes - (maxBytes % sizeof(char));
        ReadOnlySpan<byte> bytes = ReadSpan(evenBytes);
        string text = Encoding.Unicode.GetString(bytes);
        int terminator = text.IndexOf('\0');
        return terminator >= 0 ? text[..terminator] : text;
    }

    public string ReadUtf8(int byteLength)
    {
        ReadOnlySpan<byte> bytes = ReadSpan(byteLength);
        int terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
            bytes = bytes[..terminator];
        return Encoding.UTF8.GetString(bytes);
    }

    public ReadOnlySpan<byte> ReadRemainingBytes()
    {
        ReadOnlySpan<byte> remaining = _buffer[Position..];
        Position = _buffer.Length;
        return remaining;
    }

    private void EnsureAvailable(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Read length must be non-negative.");

        if (Position + length > _buffer.Length)
            throw new EndOfStreamException("The packet ended before the requested data could be read.");
    }
}
