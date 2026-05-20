using System.Buffers.Binary;
using YGOProSharp.Abstractions.Ocg.Enums;

namespace YGOProSharp;

public sealed class CoreMessage
{
    private readonly ReadOnlyMemory<byte> _raw;

    public CoreMessage(GameMessage message, ReadOnlyMemory<byte> raw)
    {
        Message = message;
        _raw = raw;
        Reader = new CoreMessageReader(raw);
    }

    public GameMessage Message { get; }

    public CoreMessageReader Reader { get; }

    public ReadOnlySpan<byte> CreateBufferSpan()
    {
        return _raw.Span[..Reader.Position];
    }

    public byte[] CreateBuffer()
    {
        return CreateBufferSpan().ToArray();
    }
}

public sealed class CoreMessageReader
{
    private readonly ReadOnlyMemory<byte> _buffer;

    public CoreMessageReader(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
    }

    public int Position { get; private set; }

    public byte ReadByte()
    {
        EnsureAvailable(sizeof(byte));
        return _buffer.Span[Position++];
    }

    public short ReadInt16()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(span);
    }

    public int ReadInt32()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(span);
    }

    public uint ReadUInt32()
    {
        ReadOnlySpan<byte> span = ReadSpan(sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    public ReadOnlySpan<byte> ReadSpan(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> span = _buffer.Span.Slice(Position, count);
        Position += count;
        return span;
    }

    public byte[] ReadBytes(int count)
    {
        return ReadSpan(count).ToArray();
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Read count must be non-negative.");

        if (Position + count > _buffer.Length)
            throw new EndOfStreamException("The native message ended before the requested data could be read.");
    }
}
