using System.Buffers;
using System.Buffers.Binary;

namespace YGOProSharp.Network;

public sealed class PacketFramer
{
    private ArrayBufferWriter<byte> _receiveBuffer = new();

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
        _receiveBuffer.Write(data);
    }

    public bool TryReadPacket(out byte[] packet)
    {
        ReadOnlySpan<byte> buffer = _receiveBuffer.WrittenSpan;
        packet = [];

        if (buffer.Length < HeaderSize)
            return false;

        int packetLength = HeaderSize switch
        {
            2 => BinaryPrimitives.ReadUInt16LittleEndian(buffer[..HeaderSize]),
            4 => BinaryPrimitives.ReadInt32LittleEndian(buffer[..HeaderSize]),
            _ => throw new InvalidOperationException("Unsupported header size.")
        };

        if (IsHeaderSizeIncluded)
            packetLength -= HeaderSize;

        if (packetLength < 0 || packetLength > MaxPacketLength)
            throw new InvalidDataException($"Packet length {packetLength} is outside the allowed range.");

        int frameLength = HeaderSize + packetLength;
        if (buffer.Length < frameLength)
            return false;

        packet = buffer.Slice(HeaderSize, packetLength).ToArray();
        PreserveRemaining(buffer[frameLength..]);
        return true;
    }

    public byte[] Frame(ReadOnlySpan<byte> packet)
    {
        ArrayBufferWriter<byte> writer = new(HeaderSize + packet.Length);
        WriteFrame(packet, writer);
        return writer.WrittenSpan.ToArray();
    }

    public void WriteFrame(ReadOnlySpan<byte> packet, IBufferWriter<byte> writer)
    {
        if (packet.Length > MaxPacketLength)
            throw new InvalidDataException($"Packet length {packet.Length} exceeds maximum {MaxPacketLength}.");

        int packetLength = IsHeaderSizeIncluded ? packet.Length + HeaderSize : packet.Length;

        Span<byte> header = writer.GetSpan(HeaderSize);
        if (HeaderSize == 2)
            BinaryPrimitives.WriteUInt16LittleEndian(header[..HeaderSize], checked((ushort)packetLength));
        else
            BinaryPrimitives.WriteInt32LittleEndian(header[..HeaderSize], packetLength);

        writer.Advance(HeaderSize);

        Span<byte> payload = writer.GetSpan(packet.Length);
        packet.CopyTo(payload);
        writer.Advance(packet.Length);
    }

    private void PreserveRemaining(ReadOnlySpan<byte> remaining)
    {
        if (remaining.Length == 0)
        {
            _receiveBuffer.Clear();
            return;
        }

        ArrayBufferWriter<byte> next = new(remaining.Length);
        next.Write(remaining);
        _receiveBuffer = next;
    }
}
