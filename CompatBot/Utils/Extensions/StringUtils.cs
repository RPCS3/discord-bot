using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CompatBot.Utils.Extensions;
using HomoglyphConverter;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils;

public static partial class StringUtils
{
    public static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly MemoryCache FuzzyPairCache = new(new MemoryCacheOptions {ExpirationScanFrequency = TimeSpan.FromMinutes(10)});
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(30);
    private const char StrikeThroughChar = '\u0336'; // 0x0335 = short dash, 0x0336 = long dash, 0x0337 = short slash, 0x0338 = long slash
    public const char InvisibleSpacer = '\u206a';
    public const char Nbsp = '\u00a0';
    [GeneratedRegex(@"\b(?<cat>cat)s?\b", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex KotFixPattern();

    internal static readonly HashSet<char> SpaceCharacters =
    [
        '\u00a0',
        '\u2002', '\u2003', '\u2004', '\u2005', '\u2006',
        '\u2007', '\u2008', '\u2009', '\u200a', '\u200b',
        '\u200c', '\u200d', '\u200e', '\u200f',
        '\u2028', '\u2029', '\u202a', '\u202b',
        '\u202c', '\u202d', '\u202e', '\u202f',
        '\u205f', '\u2060', '\u2061', '\u2062', '\u2063',
        '\u2064', '\u2065', '\u2066', '\u2067', '\u2068',
        '\u2069', '\u206a', '\u206b', '\u206c', '\u206d',
        '\u206e', '\u206f',
        '\u3000', '\u303f',
    ];

    public static string StripMarks(this string str)
    {
        return str.Replace("(R)", " ", StringComparison.InvariantCultureIgnoreCase)
            .Replace("®", " ")
            .Replace("(TM)", " ", StringComparison.InvariantCultureIgnoreCase)
            .Replace("™", " ")
            .Replace("  ", " ")
            .Replace(" : ", ": ")
            .Trim();
    }

    public static string StripQuotes(this string str)
    {
        if (str.Length < 2)
            return str;

        if (str.StartsWith('"') && str.EndsWith('"'))
            return str[1..^1];
        return str;
    }

    public static string FixTypography(this string str)
    {
        // see https://www.typewolf.com/cheatsheet and https://practicaltypography.com/apostrophes.html
        str = str
            .Replace("...", "…")
            .Replace("        ", "    ")
            .Replace("    -- ", "    —") // em
            .Replace("    --", "    —") // em
            .Replace("    ― ", "    —") // em
            .Replace("    ―", "    —") // em
            .Replace(" -- ", " – ") // en
            .Replace("--", "—") // em
            .Replace("'n'", "’n’")
            .Replace("'Tis", "’Tis")
            .Replace("'Kay", "’Kay")
            .Replace("'er", "’er")
            .Replace("'em", "’em")
            .Replace("'cause", "’cause")
            .Replace("'til", "’til")
            .Replace("'tis", "’tis")
            .Replace("'twere", "’twere")
            .Replace("'twould", "’twould")
            .Replace("'bout", "’bout")
            .Replace('`', '‘');
        var result = new StringBuilder(str.Length);
        for (var i = 0; i < str.Length; i++)
        {
            var chr = str[i] switch
            {
                '\'' when i == 0 => '‘',
                '\'' when i == str.Length => '’',
                '\'' when char.IsNumber(str[i + 1]) => '’',
                '\'' when char.IsWhiteSpace(str[i - 1]) => '‘',
                '\'' => '’',
                '"' when i == 0 => '“',
                '"' when i == str.Length => '”',
                '"' when char.IsWhiteSpace(str[i - 1]) => '“',
                '"' => '”',
                char c => c,
            };
            result.Append(chr);
        }
        return result.ToString();
    }

    public static string FixKot(this string str)
    {
        var matches = KotFixPattern().Matches(str);
        foreach (Match m in matches)
        {
            var idx = m.Index;
            var end = idx + 3;
            str = $"{str[..idx]}kot{str[end..]}";
        }
        return str;
    }

    public static string TrimEager(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        int start, end;
        for (start = 0; start < str.Length; start++)
        {
            if (char.IsWhiteSpace(str, start) || IsFormat(str[start]))
                continue;

            if (char.IsHighSurrogate(str, start)
                && char.GetUnicodeCategory(str, start) == UnicodeCategory.OtherNotAssigned
                && str[start] >= 0xdb40) // this will check if the surrogate pair is >= E0000 (see https://en.wikipedia.org/wiki/UTF-16#U+010000_to_U+10FFFF)
                continue;

            if (char.IsLowSurrogate(str, start))
                continue;

            break;
        }

        for (end = str.Length - 1; end >= start; end--)
        {
            if (char.IsWhiteSpace(str, end) || IsFormat(str[end]))
                continue;

            if (char.IsLowSurrogate(str, end)
                && end > start
                && char.IsHighSurrogate(str, end - 1)
                && char.GetUnicodeCategory(str, end - 1) is UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse
                && str[end-1] >= 0xdb40)
                continue;

            if (char.IsHighSurrogate(str, end)
                && char.GetUnicodeCategory(str, end) is UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse
                && str[end] >= 0xdb40)
                continue;
                
            if (char.GetUnicodeCategory(str, end) is UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse)
                continue;

            break;
        }

        return CreateTrimmedString(str, start, end);
    }

    public static string AsString(this ReadOnlySequence<byte> buffer, Encoding? encoding = null)
    {
        encoding ??= Encoding.Latin1;
        if (buffer.IsSingleSegment)
            return encoding.GetString(buffer.First.Span);

        void Splice(Span<char> span, ReadOnlySequence<byte> sequence)
        {
            foreach (var segment in sequence)
            {
                encoding.GetChars(segment.Span, span);
                span = span[segment.Length ..];
            }
        }
        return string.Create((int)buffer.Length, buffer, Splice);
    }

    public static string ToUtf8(this string str)
        => Utf8.GetString(Encoding.Latin1.GetBytes(str));

    public static string ToLatin8BitEncoding(this string str)
    {
        try
        {
            return Encoding.Latin1.GetString(Utf8.GetBytes(str));
        }
        catch (Exception e)
        {
            Config.Log.Error(e, $"Failed to decode string from {Encoding.Latin1.EncodingName} to {Utf8.EncodingName}");
            return str;
        }
    }

    public static string ToLatin8BitRegexPattern(this Regex regex)
        => regex.ToString().ToLatin8BitRegexPattern();
        
    public static string ToLatin8BitRegexPattern(this string regexPattern)
    {
        var encoder = Utf8.GetEncoder();
        Span<byte> tmp = stackalloc byte[4];
        var span = regexPattern.AsSpan();
        var result = new StringBuilder(regexPattern.Length);
        while (!span.IsEmpty)
        {
            var count = encoder.GetBytes(span[..1], tmp, false);
            if (count == 1)
                result.Append(Encoding.Latin1.GetString(tmp[..count]));
            else if (count > 1)
            {
                result.Append('(');
                for (var i = 0; i < count; i++)
                    result.Append(@"\x").Append(Utf8ToLatin1RegexPatternEncoderFallback.CustomMapperFallbackBuffer.ByteToHex[tmp[i]]);
                result.Append(')');
            }
            span = span[1..];
        }
        return result.ToString();
    }

    public static string GetSuffix(long num) => num == 1 ? "" : "s";

    public static string FixSpaces(this string text)
        => text.Replace(" ", " " + InvisibleSpacer)
            .Replace("`", InvisibleSpacer + "`")
            .Replace(Environment.NewLine, "\n");

    public static int GetVisibleLength(this string? s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;

        var c = 0;
        var e = StringInfo.GetTextElementEnumerator(s.Normalize());
        while (e.MoveNext())
        {
            var strEl = e.GetTextElement();
            foreach (var chr in strEl)
            {
                var category = char.GetUnicodeCategory(chr);
                if (char.IsControl(chr)
                    || category == UnicodeCategory.Format
                    || category == UnicodeCategory.ModifierSymbol
                    || category == UnicodeCategory.NonSpacingMark
                    || char.IsHighSurrogate(chr)
                    || chr == StrikeThroughChar)
                    continue;
                    
                c++;
            }
        }
        return c;
    }

    public static string StripInvisibleAndDiacritics(this string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;

        var e = StringInfo.GetTextElementEnumerator(s.Normalize(NormalizationForm.FormD));
        var result = new StringBuilder();
        while (e.MoveNext())
        {
            var strEl = e.GetTextElement();
            foreach (var ch in strEl)
                switch (char.GetUnicodeCategory(ch))
                {
                    case UnicodeCategory.Control:
                    case UnicodeCategory.EnclosingMark:
                    case UnicodeCategory.ConnectorPunctuation:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.SpacingCombiningMark:
                        continue;
                    default:
                        if (ch == StrikeThroughChar)
                            continue;
                        result.Append(ch);
                        break;
                }
        }
        return result.ToString();
    }

    public static string TrimVisible(this string s, int maxLength)
    {
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
            if (char.IsControl(strEl[0]) || char.GetUnicodeCategory(strEl[0]) == UnicodeCategory.Format || strEl[0] == StrikeThroughChar)
                continue;

            c++;
        }
        return result.Append('…').ToString();
    }

    public static string PadLeftVisible(this string s, int totalWidth, char padding = ' ')
    {
        var valueWidth = s.GetVisibleLength();
        var diff = s.Length - valueWidth;
        totalWidth += diff;
        return s.PadLeft(totalWidth, padding);
    }

    public static string PadRightVisible(this string s, int totalWidth, char padding = ' ')
    {
        var valueWidth = s.GetVisibleLength();
        var diff = s.Length - valueWidth;
        totalWidth += diff;
        return s.PadRight(totalWidth, padding);
    }

    public static string StrikeThrough(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var result = new StringBuilder(str.Length*2);
        result.Append(StrikeThroughChar);
        foreach (var c in str)
        {
            result.Append(c);
            if (char.IsLetterOrDigit(c) || char.IsLowSurrogate(c))
                result.Append(StrikeThroughChar);
        }
        return result.ToString(0, result.Length-1);
    }

    public static string GetMoons(decimal? stars, bool haveFun = true)
    {
        if (!stars.HasValue)
            return "";

        var fullStars = (int)stars;
        var halfStar = (int)Math.Round((stars.Value - fullStars)*4, MidpointRounding.ToEven);
        var noStars = 5 - (halfStar > 0 && halfStar <= 4 ? 1 : 0) - fullStars;
        var result = "";
        for (var i = 0; i < fullStars; i++)
            result += "🌕";

        if (halfStar == 4)
        {
            if (haveFun && new Random().Next(100) == 69)
                result += "🌝";
            else
                result += "🌕";
        }
        else if (halfStar == 3)
            result += "🌖";
        else if (halfStar == 2)
            result += "🌗";
        else if (halfStar == 1)
            result += "🌘";

        for (var i = 0; i < noStars; i++)
        {
            if (haveFun && i == 0 && halfStar == 0 && new Random().Next(100) == 69)
                result += "🌚";
            else
                result += "🌑";
        }
        return result;
    }

    public static string GetStars(decimal? stars)
    {
        if (!stars.HasValue)
            return "";

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

    internal static string GetAcronym(this string? str, bool includeAllCaps = false, bool includeAllDigits = false)
    {
        if (string.IsNullOrEmpty(str))
            return "";

        var result = "";
        var previousWasLetter = false;
        foreach (var c in str)
        {
            var isLetter = char.IsLetterOrDigit(c);
            if ((isLetter && !previousWasLetter)
                || (includeAllCaps && char.IsUpper(c))
                || (includeAllDigits && char.IsDigit(c)))
                result += c;
            previousWasLetter = isLetter;
        }

        return result;
    }

    internal static double GetFuzzyCoefficientCached(this string? strA, string? strB)
    {
        strA = strA?.ToLowerInvariant() ?? "";
        strB = strB?.ToLowerInvariant() ?? "";
        var cacheKey = GetFuzzyCacheKey(strA, strB);
        if (!FuzzyPairCache.TryGetValue(cacheKey, out FuzzyCacheValue? match)
            || match is null
            || strA != match.StrA
            || strB != match.StrB)
            match = new FuzzyCacheValue
            {
                StrA = strA,
                StrB = strB,
                Coefficient = strA.ToCanonicalForm().GetScoreWithAcronym(strB.ToCanonicalForm()),
            };
        FuzzyPairCache.Set(cacheKey, match, CacheTime);
        return match.Coefficient;
    }

    internal static bool EqualsIgnoringDiacritics(this string strA, string strB)
    {
        var a = strA.ToCanonicalForm();
        var b = strB.ToCanonicalForm();
        return string.Compare(a, b, CultureInfo.InvariantCulture, CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth | CompareOptions.IgnoreCase) == 0;
    }

    internal static int GetStableHash(this string str)
    {
        var data = Encoding.UTF8.GetBytes(str.ToLowerInvariant());
        var hash = SHA256.HashData(data);
        return BitConverter.ToInt32(hash, 0);
    }

    private static double GetScoreWithAcronym(this string strA, string strB)
    {
        var fullMatch = strA.DiceIshCoefficientIsh(strB);
        var acronymMatch = strA.DiceIshCoefficientIsh(strB.GetAcronym().ToLowerInvariant());
        return Math.Max(fullMatch, acronymMatch);
    }

    private static (long, int) GetFuzzyCacheKey(string strA, string strB)
    {
        var hashPair = (((long)strA.GetHashCode()) << (sizeof(int) * 8)) | (strB.GetHashCode() & uint.MaxValue);
        var lengthPair = (strA.Length << (sizeof(short) * 8)) | (strB.Length & ushort.MaxValue);
        return (hashPair, lengthPair);
    }

    private class FuzzyCacheValue
    {
        public string? StrA;
        public string? StrB;
        public double Coefficient;
    }
}