using System;
using System.Buffers;
using System.Linq;
using System.Text;

namespace CompatBot.Utils
{
    internal static class StringUtils
    {
        private static readonly Encoding Latin8BitEncoding = Encoding.GetEncodings()
                                                                 .FirstOrDefault(e => e.CodePage == 1250 || e.CodePage == 1252 || e.CodePage == 28591)?
                                                                 .GetEncoding()
                                                             ?? Encoding.ASCII;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static string StripQuotes(this string str)
        {
            if (str == null || str.Length < 2)
                return str;

            if (str.StartsWith('"') && str.EndsWith('"'))
                return str.Substring(1, str.Length - 2);
            return str;
        }

        public static string AsString(this ReadOnlySequence<byte> buffer, Encoding encoding = null)
        {
            encoding = encoding ?? Latin8BitEncoding;
            if (buffer.IsSingleSegment)
                return encoding.GetString(buffer.First.Span);

            void Splice(Span<char> span, ReadOnlySequence<byte> sequence)
            {
                foreach (var segment in sequence)
                {
                    encoding.GetChars(segment.Span, span);
                    span = span.Slice(segment.Length);
                }
            }
            return string.Create((int)buffer.Length, buffer, Splice);
        }

        public static string ToUtf8(this string str)
        {
            return Utf8.GetString(Latin8BitEncoding.GetBytes(str));
        }

        public static string ToLatin8BitEncoding(this string str)
        {
            try
            {
                return Latin8BitEncoding.GetString(Utf8.GetBytes(str));
            }
            catch (Exception e)
            {
                Config.Log.Error(e, $"Failed to decode string from {Latin8BitEncoding.EncodingName} to {Utf8.EncodingName}");
                return str;
            }
        }

        public static string GetSuffix(long num) => num % 10 == 1 && num % 100 != 11 ? "" : "s";

        public static string FixSpaces(this string text) => text?.Replace("  ", " \u200d \u200d");
    }
}
