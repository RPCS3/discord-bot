using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HomoglyphConverter
{
    public static class Normalizer
    {
        private static readonly Encoding Utf32 = new UTF32Encoding(false, false, true);

        private static readonly Dictionary<string, string> HomoglyphSequences = new()
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
        };

        // as per https://www.unicode.org/reports/tr39/#Confusable_Detection
        private static string ToSkeletonString(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // step 1: Convert X to NFD format, as described in [UAX15].
            input = input.Normalize(NormalizationForm.FormD);
            // step 2: Concatenate the prototypes for each character in X according to the specified data, producing a string of exemplar characters.
            input = ReplaceConfusables(input);
            // ste 3: Reapply NFD.
            return input.Normalize(NormalizationForm.FormD);
        }

        public static string ToCanonicalForm(this string input)
        {
            if (string.IsNullOrEmpty(input))
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
            var uintInput = convertedLength < 256 / sizeof(uint) ? stackalloc uint[convertedLength] : new uint[convertedLength];
            for (var i = 0; i < uintInput.Length; i++)
                uintInput[i] = BitConverter.ToUInt32(utf32Input, i * 4);
            var result = new List<uint>(convertedLength);
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
}
