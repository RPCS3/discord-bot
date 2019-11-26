using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HomoglyphConverter
{
    public static class Normalizer
    {
        private static readonly Dictionary<uint, uint[]> Mapping = ConfusablesBuilder.Build();
        private static readonly Encoding Utf32 = new UTF32Encoding(false, false, true);

        private static readonly Dictionary<string, string> HomoglyphSequences = new Dictionary<string, string>
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
        };

        // as per http://www.unicode.org/reports/tr39/#Confusable_Detection
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
            var result = ReplaceMultiletterConfusables(input);
            for (var i = 0; result != input && i < 128; i++)
            {
                input = result;
                result = ReplaceMultiletterConfusables(input);
            }
            return result;
        }

        private static string ReplaceMultiletterConfusables(string input)
        {
            foreach (var pair in HomoglyphSequences)
                input = input.Replace(pair.Key, pair.Value);
            return input;
        }

        private static string ReplaceConfusables(string input)
        {
            var utf32Input = Utf32.GetBytes(input);
            var uintInput = new uint[utf32Input.Length / 4];
            for (var i = 0; i < uintInput.Length; i++)
                uintInput[i] = BitConverter.ToUInt32(utf32Input, i * 4);
            var result = new List<uint>(uintInput.Length);
            foreach (var ch in uintInput)
            {
                if (Mapping.TryGetValue(ch, out var replacement))
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
