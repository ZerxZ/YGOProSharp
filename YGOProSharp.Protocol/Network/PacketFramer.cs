using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace YGOProSharp.Protocol;

public sealed class PacketFramer
{
    private byte[] _buffer = [];
    private int _readOffset;
    private int _writeOffset;

    public PacketFramer(int maxPacketLength = 0xFFFF, int headerSize = 2, bool isHeaderSizeIncluded = false)
    {
        if (headerSize is not 2 and not 4)
            throw new ArgumentOutOfRangeException(nameof(headerSize), headerSize, "Header size must be 2 or 4 bytes.");

        MaxPacketLength = maxPacketLength;
        HeaderSize = headerSize;
        IsHeaderSizeIncluded = isHeaderSizeIncluded;
    }

    public int MaxPacketLength { get; }

    public int HeaderSize { get; }

    public bool IsHeaderSizeIncluded { get; }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_writeOffset));
        _writeOffset += data.Length;
    }

    public bool TryReadPacket(out byte[] packet)
    {
        int available = _writeOffset - _readOffset;
        packet = [];

        if (available < HeaderSize)
            return false;

        ReadOnlySpan<byte> span = _buffer.AsSpan(_readOffset, available);
        int packetLength = HeaderSize switch
        {
            2 => BinaryPrimitives.ReadUInt16LittleEndian(span[..HeaderSize]),
            4 => BinaryPrimitives.ReadInt32LittleEndian(span[..HeaderSize]),
            _ => throw new InvalidOperationException("Unsupported header size.")
        };

        if (IsHeaderSizeIncluded)
            packetLength -= HeaderSize;

        if (packetLength < 0 || packetLength > MaxPacketLength)
            throw new InvalidDataException($"Packet length {packetLength} is outside the allowed range.");

        int frameLength = HeaderSize + packetLength;
        if (available < frameLength)
            return false;

        packet = packetLength == 0 ? [] : GC.AllocateUninitializedArray<byte>(packetLength);
        if (packetLength > 0)
            span.Slice(HeaderSize, packetLength).CopyTo(packet);

        _readOffset += frameLength;
        if (_readOffset == _writeOffset)
        {
            _readOffset = 0;
            _writeOffset = 0;
        }

        return true;
    }

    public byte[] Frame(ReadOnlySpan<byte> packet)
    {
        if (packet.Length > MaxPacketLength)
            throw new InvalidDataException($"Packet length {packet.Length} exceeds maximum {MaxPacketLength}.");

        byte[] frame = GC.AllocateUninitializedArray<byte>(HeaderSize + packet.Length);
        WriteHeader(frame, packet.Length);
        packet.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    public void WriteFrame(ReadOnlySpan<byte> packet, IBufferWriter<byte> writer)
    {
        if (packet.Length > MaxPacketLength)
            throw new InvalidDataException($"Packet length {packet.Length} exceeds maximum {MaxPacketLength}.");

        int totalLength = HeaderSize + packet.Length;
        Span<byte> target = writer.GetSpan(totalLength);
        WriteHeader(target, packet.Length);
        packet.CopyTo(target.Slice(HeaderSize, packet.Length));
        writer.Advance(totalLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteHeader(Span<byte> destination, int payloadLength)
    {
        int packetLength = IsHeaderSizeIncluded ? payloadLength + HeaderSize : payloadLength;
        if (HeaderSize == 2)
            BinaryPrimitives.WriteUInt16LittleEndian(destination, checked((ushort)packetLength));
        else
            BinaryPrimitives.WriteInt32LittleEndian(destination, packetLength);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        int remaining = _writeOffset - _readOffset;

        // 优先原地压缩复用已有缓冲区，避免粘包时反复分配新数组。
        if (_readOffset > 0 && _buffer.Length - remaining >= additionalBytes)
        {
            if (remaining > 0)
                _buffer.AsSpan(_readOffset, remaining).CopyTo(_buffer);

            _readOffset = 0;
            _writeOffset = remaining;
            return;
        }

        if (_buffer.Length - _writeOffset >= additionalBytes)
            return;

        int required = checked(remaining + additionalBytes);
        int nextSize = _buffer.Length == 0 ? Math.Max(256, required) : Math.Max(_buffer.Length * 2, required);
        byte[] next = GC.AllocateUninitializedArray<byte>(nextSize);
        if (remaining > 0)
            _buffer.AsSpan(_readOffset, remaining).CopyTo(next);

        _buffer = next;
        _readOffset = 0;
        _writeOffset = remaining;
    }
}
