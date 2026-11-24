using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace YGOProSharp.Protocol.Utils
{
    public static class BinaryExtensions
    {
        private const int StackAllocThreshold = 1024;

        // 固定长度 UTF-16 字符串（fixed length UTF-16 string），优先 stackalloc 零堆分配写入。
        public static void WriteUnicode(this BinaryWriter writer, string text, int len)
        {
            int byteLength = len * sizeof(char);
            if (byteLength <= 0)
                return;

            ReadOnlySpan<char> source = text.AsSpan();
            int maxChars = Math.Max(0, len - 1);
            if (source.Length > maxChars)
            {
#if DEBUG
                throw new ArgumentException("String '" + text + "' is too long for fixed length " + len + ".");
#else
                source = source[..maxChars];
#endif
            }

            byte[]? rented = byteLength > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(byteLength) : null;
            try
            {
                Span<byte> buffer = rented is not null ? rented.AsSpan(0, byteLength) : stackalloc byte[byteLength];
                buffer.Clear();
                if (!source.IsEmpty)
                    Encoding.Unicode.GetBytes(source, buffer);
                writer.Write(buffer);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        // 可变长度 UTF-16 字符串（variable length UTF-16 string），含结尾 '\0'。
        public static void WriteUnicodeAutoLength(this BinaryWriter writer, string text, int maxlen)
        {
            if (maxlen <= 0)
                return;

            ReadOnlySpan<char> source = text.AsSpan();
            int maxChars = maxlen - 1;
            if (source.Length > maxChars)
            {
#if DEBUG
                throw new ArgumentException("String '" + text + "' is too long for max length " + maxlen + ".");
#else
                source = source[..maxChars];
#endif
            }

            int byteLength = (source.Length + 1) * sizeof(char);
            byte[]? rented = byteLength > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(byteLength) : null;
            try
            {
                Span<byte> buffer = rented is not null ? rented.AsSpan(0, byteLength) : stackalloc byte[byteLength];
                int written = source.IsEmpty ? 0 : Encoding.Unicode.GetBytes(source, buffer);
                buffer[written] = 0;
                buffer[written + 1] = 0;
                writer.Write(buffer[..(written + sizeof(char))]);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static string ReadUnicode(this BinaryReader reader, int len)
        {
            int byteLength = len * sizeof(char);
            if (byteLength <= 0)
                return string.Empty;

            byte[]? rented = byteLength > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(byteLength) : null;
            try
            {
                Span<byte> buffer = rented is not null ? rented.AsSpan(0, byteLength) : stackalloc byte[byteLength];
                int read = ReadUpTo(reader, buffer);
                int evenBytes = read - (read % sizeof(char));
                if (evenBytes == 0)
                    return string.Empty;

                int stringBytes = evenBytes;
                for (int i = 0; i < evenBytes; i += sizeof(char))
                {
                    if (buffer[i] == 0 && buffer[i + 1] == 0)
                    {
                        stringBytes = i;
                        break;
                    }
                }

                return stringBytes == 0 ? string.Empty : Encoding.Unicode.GetString(buffer[..stringBytes]);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static void WriteUtf8(this BinaryWriter writer, string text, int byteLength)
        {
            if (byteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Fixed UTF-8 length must be non-negative.");

            if (byteLength == 0)
                return;

            byte[]? rented = byteLength > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(byteLength) : null;
            try
            {
                Span<byte> buffer = rented is not null ? rented.AsSpan(0, byteLength) : stackalloc byte[byteLength];
                buffer.Clear();

                ReadOnlySpan<char> source = text.AsSpan();
                int maxPayload = byteLength - 1;
                if (!source.IsEmpty && maxPayload > 0)
                {
                    int encodedCount = Encoding.UTF8.GetByteCount(source);
#if DEBUG
                    if (encodedCount > maxPayload)
                        throw new ArgumentException("String '" + text + "' is too long for fixed UTF-8 length " + byteLength + ".");
#endif
                    if (encodedCount <= maxPayload)
                    {
                        Encoding.UTF8.GetBytes(source, buffer[..maxPayload]);
                    }
                    else
                    {
                        byte[]? temp = encodedCount > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(encodedCount) : null;
                        try
                        {
                            Span<byte> utf8 = temp is not null ? temp.AsSpan(0, encodedCount) : stackalloc byte[encodedCount];
                            Encoding.UTF8.GetBytes(source, utf8);
                            utf8[..maxPayload].CopyTo(buffer);
                        }
                        finally
                        {
                            if (temp is not null)
                                ArrayPool<byte>.Shared.Return(temp);
                        }
                    }
                }

                writer.Write(buffer);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static string ReadUtf8(this BinaryReader reader, int byteLength)
        {
            if (byteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Fixed UTF-8 length must be non-negative.");

            if (byteLength == 0)
                return string.Empty;

            byte[]? rented = byteLength > StackAllocThreshold ? ArrayPool<byte>.Shared.Rent(byteLength) : null;
            try
            {
                Span<byte> buffer = rented is not null ? rented.AsSpan(0, byteLength) : stackalloc byte[byteLength];
                int read = ReadUpTo(reader, buffer);
                ReadOnlySpan<byte> bytes = buffer[..read];
                int index = bytes.IndexOf((byte)0);
                if (index >= 0)
                    bytes = bytes[..index];
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public static byte[] ReadToEnd(this BinaryReader reader)
        {
            return reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        }

        private static int ReadUpTo(BinaryReader reader, Span<byte> destination)
        {
            int total = 0;
            while (total < destination.Length)
            {
                int read = reader.Read(destination[total..]);
                if (read == 0)
                    break;
                total += read;
            }

            return total;
        }
    }
}
