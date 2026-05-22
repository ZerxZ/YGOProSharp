using System;
using System.IO;
using System.Text;

namespace YGOProSharp.Protocol.Utils
{
    public static class BinaryExtensions
    {
        // 固定长度 UTF-16 字符串（fixed length UTF-16 string）。
        public static void WriteUnicode(this BinaryWriter writer, string text, int len)
        {
            byte[] unicode = Encoding.Unicode.GetBytes(text);
            byte[] result = new byte[len * 2];
            int copy = unicode.Length;
            if (unicode.Length > len * 2 - 2)
            {
                copy = len * 2 - 2;
#if DEBUG
                throw new ArgumentException("String '" + text + "' is too long for fixed length " + len + ".");
#endif
            }
            Array.Copy(unicode, result, copy);
            writer.Write(result);
        }

        // 可变长度 UTF-16 字符串（variable length UTF-16 string）。
        public static void WriteUnicodeAutoLength(this BinaryWriter writer, string text, int maxlen)
        {
            byte[] result = Encoding.Unicode.GetBytes(text + "\0");
            int len = result.Length / 2;
            if (len > maxlen)
            {
                len = maxlen;
                result[len * 2 - 2] = 0;
                result[len * 2 - 1] = 0;
#if DEBUG
                throw new ArgumentException("String '" + text + "' is too long for max length " + maxlen + ".");
#endif
            }
            writer.Write(result, 0, len * 2);
        }

        public static string ReadUnicode(this BinaryReader reader, int len)
        {
            byte[] unicode = reader.ReadBytes(len * 2);
            string text = Encoding.Unicode.GetString(unicode);
            int index = text.IndexOf('\0');
            if (index >= 0)
                text = text.Substring(0, index);
            return text;
        }

        public static void WriteUtf8(this BinaryWriter writer, string text, int byteLength)
        {
            if (byteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Fixed UTF-8 length must be non-negative.");

            byte[] result = new byte[byteLength];
            if (byteLength == 0)
            {
                writer.Write(result);
                return;
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            int copyLength = Math.Min(utf8.Length, byteLength - 1);
            Array.Copy(utf8, result, copyLength);
#if DEBUG
            if (utf8.Length > byteLength - 1)
                throw new ArgumentException("String '" + text + "' is too long for fixed UTF-8 length " + byteLength + ".");
#endif
            writer.Write(result);
        }

        public static string ReadUtf8(this BinaryReader reader, int byteLength)
        {
            if (byteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Fixed UTF-8 length must be non-negative.");

            byte[] bytes = reader.ReadBytes(byteLength);
            int index = Array.IndexOf(bytes, (byte)0);
            int count = index >= 0 ? index : bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, count);
        }

        public static byte[] ReadToEnd(this BinaryReader reader)
        {
            return reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
        }
    }
}
