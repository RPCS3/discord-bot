using System;
using System.Buffers;
using System.Text;

namespace CompatBot.Utils
{
    internal static class StringUtils
    {
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
            encoding = encoding ?? Encoding.ASCII;
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

        public static string FixSpaces(this string text) => text?.Replace("  ", " \u200d \u200d");
    }
}
