using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DuoVia.FuzzyStrings;
using HomoglyphConverter;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils
{
    public static class StringUtils
    {
        private static readonly Encoding Latin8BitEncoding = Encoding.GetEncodings()
                                                                 .FirstOrDefault(e => e.CodePage == 1250 || e.CodePage == 1252 || e.CodePage == 28591)?
                                                                 .GetEncoding()
                                                             ?? Encoding.ASCII;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly MemoryCache FuzzyPairCache = new MemoryCache(new MemoryCacheOptions {ExpirationScanFrequency = TimeSpan.FromMinutes(10)});

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

        public static string StripMarks(this string str)
        {
            return str.Replace("(R)", "", StringComparison.InvariantCultureIgnoreCase)
                .Replace("®", "", StringComparison.InvariantCultureIgnoreCase)
                .Replace("(TM)", "", StringComparison.InvariantCultureIgnoreCase)
                .Replace("™", "", StringComparison.InvariantCultureIgnoreCase);
        }

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

        public static string GetSuffix(long num) => num == 1 ? "" : "s";

        public static string FixSpaces(this string text) => text?.Replace(" ", " \u200d");

        public static int GetVisibleLength(this string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            var c = 0;
            var e = StringInfo.GetTextElementEnumerator(s.Normalize());
            while (e.MoveNext())
            {
                var strEl = e.GetTextElement();
                if (char.IsControl(strEl[0]) || char.GetUnicodeCategory(strEl[0]) == UnicodeCategory.Format)
                    continue;

                c++;
            }
            return c;
        }

        public static string TrimVisible(this string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            if (maxLength < 1)
                throw new ArgumentException("Max length can't be less than 1", nameof(maxLength));

            if (s.Length <= maxLength)
                return s;

            var c = 0;
            var e = StringInfo.GetTextElementEnumerator(s.Normalize());
            var result = new StringBuilder();
            while (e.MoveNext() && c < maxLength-1)
            {
                var strEl = e.GetTextElement();
                result.Append(strEl);
                if (char.IsControl(strEl[0]) || char.GetUnicodeCategory(strEl[0]) == UnicodeCategory.Format)
                    continue;

                c++;
            }
            return result.Append("…").ToString();
        }

        public static string PadLeftVisible(this string s, int totalWidth, char padding = ' ')
        {
            s = s ?? "";
            var valueWidth = s.GetVisibleLength();
            var diff = s.Length - valueWidth;
            totalWidth += diff;
            return s.PadLeft(totalWidth, padding);
        }

        public static string PadRightVisible(this string s, int totalWidth, char padding = ' ')
        {
            s = s ?? "";
            var valueWidth = s.GetVisibleLength();
            var diff = s.Length - valueWidth;
            totalWidth += diff;
            return s.PadRight(totalWidth, padding);
        }

        public static string GetMoons(decimal? stars)
        {
            if (!stars.HasValue)
                return null;

            var fullStars = (int)stars;
            var halfStar = Math.Round((stars.Value - fullStars)*4, MidpointRounding.ToEven);
            var noStars = 5 - (halfStar > 0 && halfStar <= 4 ? 1 : 0) - fullStars;
            var result = "";
            for (var i = 0; i < fullStars; i++)
                result += "🌕";

            if (halfStar > 3)
                result += "🌕";
            else if (halfStar > 2)
                result += "🌖";
            else if (halfStar > 1)
                result += "🌗";
            else if (halfStar > 0)
                result += "🌘";

            for (var i = 0; i < noStars; i++)
                result += "🌑";
            return result;
        }

        public static string GetStars(decimal? stars)
        {
            if (!stars.HasValue)
                return null;

            var fullStars = (int)Math.Round(stars.Value, MidpointRounding.ToEven);
            var noStars = 5 - fullStars;
            var result = "";
            for (var i = 0; i < fullStars; i++)
                result += "★";
            for (var i = 0; i < noStars; i++)
                result += "☆";
            return result;
        }

        private static bool IsFormat(char c) => SpaceCharacters.Contains(c);

        private static string CreateTrimmedString(string str, int start, int end)
        {
            var len = end - start + 1;
            if (len == str.Length)
                return str;

            return len == 0 ? "" : str.Substring(start, len);
        }

        internal static string GetAcronym(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            var result = "";
            bool previousWasLetter = false;
            foreach (var c in str)
            {
                var isLetter = char.IsLetterOrDigit(c);
                if (isLetter && !previousWasLetter)
                    result += c;
                previousWasLetter = isLetter;
            }
            return result;
        }

        internal static double GetFuzzyCoefficientCached(this string strA, string strB)
        {
            strA = strA?.ToLowerInvariant() ?? "";
            strB = strB?.ToLowerInvariant() ?? "";
            var cacheKey = GetFuzzyCacheKey(strA, strB);
            if (!FuzzyPairCache.TryGetValue(cacheKey, out FuzzyCacheValue match)
                || strA != match.StrA
                || strB != match.StrB)
                match = new FuzzyCacheValue
                {
                    StrA = strA,
                    StrB = strB,
                    Coefficient = Normalizer.ToCanonicalForm(strA).GetScoreWithAcronym(Normalizer.ToCanonicalForm(strB)),
                };
            FuzzyPairCache.Set(cacheKey, match);
            return match.Coefficient;
        }

        private static double GetScoreWithAcronym(this string strA, string strB)
        {
            return Math.Max(
                strA.DiceCoefficient(strB),
                strA.DiceCoefficient(strB.GetAcronym().ToLowerInvariant())
            );
        }

        private static (long, int) GetFuzzyCacheKey(string strA, string strB)
        {
            var hashPair = (((long) (strA.GetHashCode())) << (sizeof(int) * 8)) | (((long) strB.GetHashCode()) & ((long) uint.MaxValue));
            var lengthPair = (strA.Length << (sizeof(short) * 8)) | (strB.Length & ushort.MaxValue);
            return (hashPair, lengthPair);
        }

        private class FuzzyCacheValue
        {
            public string StrA;
            public string StrB;
            public double Coefficient;
        }
    }
}
