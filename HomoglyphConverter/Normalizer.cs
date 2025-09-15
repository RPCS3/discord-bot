using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace HomoglyphConverter;

public static class Normalizer
{
    private static readonly Encoding Utf32 = new UTF32Encoding(false, false, true);

    private static readonly FrozenDictionary<string, string> HomoglyphSequences = new Dictionary<string, string>
    {
        ["rn"] = "m",
        ["cl"] = "d",
        //["cj"] = "g",
        ["vv"] = "w",
        ["VV"] = "W",
        ["\\/"] = "V",
        ["\\Л/"] = "W",
        ["}|{"] = "Ж",
        ["wv"] = "vw",
        ["WV"] = "VW",
        ["ĸ"] = "k",
        ["◌"] = "o",
    }.ToFrozenDictionary();

    // as per https://www.unicode.org/reports/tr39/#Confusable_Detection
    [return: NotNullIfNotNull(nameof(input))]
    private static string? ToSkeletonString(this string? input)
    {
        if (input is null or "")
            return input;

        // step 1: Convert X to NFD format, as described in [UAX15].
        input = input.Normalize(NormalizationForm.FormD);
        // step 2: Concatenate the prototypes for each character in X according to the specified data, producing a string of exemplar characters.
        input = ReplaceConfusables(input);
        // ste 3: Reapply NFD.
        return input.Normalize(NormalizationForm.FormD);
    }

    [return: NotNullIfNotNull(nameof(input))]
    public static string? ToCanonicalForm(this string? input)
    {
        if (input is null or "")
            return input;

        input = ToSkeletonString(input);
        var result = ReplaceMultiLetterConfusables(input);
        for (var i = 0; result != input && i < 128; i++)
        {
            input = result;
            result = ReplaceMultiLetterConfusables(input);
        }
        return result;
    }

    private static string ReplaceMultiLetterConfusables(string input)
    {
        foreach (var (sequence, replacement) in HomoglyphSequences)
            input = input.Replace(sequence, replacement);
        return input;
    }

    private static string ReplaceConfusables(string input)
    {
        var utf32Input = Utf32.GetBytes(input);
        var convertedLength = utf32Input.Length / 4;
        var uintInput = convertedLength < 256 / sizeof(int) ? stackalloc int[convertedLength] : new int[convertedLength];
        for (var i = 0; i < uintInput.Length; i++)
            uintInput[i] = BitConverter.ToInt32(utf32Input, i * 4);
        var result = new List<int>(convertedLength);
        foreach (var ch in uintInput)
        {
            if (Confusables.Mapping.TryGetValue(ch, out var replacement))
                result.AddRange(replacement);
            else
                result.Add(ch);
        }
        var resultBytes = (
            from ch in result
            from b in BitConverter.GetBytes(ch)
            select b
        ).ToArray();
        return Utf32.GetString(resultBytes);
    }
}