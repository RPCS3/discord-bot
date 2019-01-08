using System;
using System.Buffers;
using System.Collections.Generic;
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

        private static readonly HashSet<char> SpaceCharacters = new HashSet<char>
        {
            '\u00a0',
            '\u2002', '\u2003', '\u2004', '\u2005', '\u2006',
            '\u2007', '\u2008', '\u2009', '\u200a', '\u200b',
            '\u200c', '\u200d', '\u200e', '\u200f',
            '\u2028', '\u2029', '\u202a', '\u202b', '\u202c',
            '\u202c', '\u202d', '\u202e', '\u202f',
            '\u205f', '\u2060', '\u2061', '\u2062', '\u2063',
            '\u2064', '\u2065', '\u2066', '\u2067', '\u2068',
            '\u2069', '\u206a', '\u206b', '\u206c', '\u206d',
            '\u206e', '\u206f',
            '\u3000', '\u303f',
        };

        public static string StripQuotes(this string str)
        {
            if (str == null || str.Length < 2)
                return str;

            if (str.StartsWith('"') && str.EndsWith('"'))
                return str.Substring(1, str.Length - 2);
            return str;
        }

        public static string TrimEager(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            int start, end;
            for (start = 0; start < str.Length; start++)
                if (!char.IsWhiteSpace(str[start]) && !IsFormat(str[start]))
                    break;

            for (end = str.Length - 1; end >= start; end--)
                if (!char.IsWhiteSpace(str[end]) && !IsFormat(str[end]))
                    break;

            return CreateTrimmedString(str, start, end);
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

        public static string FixSpaces(this string text) => text?.Replace(" ", " \u200d");

        private static bool IsFormat(char c) => SpaceCharacters.Contains(c);

        private static string CreateTrimmedString(string str, int start, int end)
        {
            var len = end - start + 1;
            if (len == str.Length)
                return str;

            return len == 0 ? "" : str.Substring(start, len);
        }
    }
}
