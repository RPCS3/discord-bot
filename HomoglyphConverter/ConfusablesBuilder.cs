using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HomoglyphConverter
{
    public static class ConfusablesBuilder
    {
        private static readonly char[] CommentSplitter = {'#'};
        private static readonly char[] FieldSplitter = {';'};
        private static readonly char[] PairSplitter = {' '};

        // requires a gzipped mapping from http://www.unicode.org/Public/security/latest/confusables.txt
        public static Dictionary<uint, uint[]> Build()
        {
            var result = new Dictionary<uint, uint[]>();
            var assembly = Assembly.GetAssembly(typeof(ConfusablesBuilder));
            var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("confusables.txt.gz", StringComparison.InvariantCultureIgnoreCase));
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, Encoding.UTF8, false);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    var lineParts = line.Split(CommentSplitter, 2);
                    var mapping = lineParts[0].Split(FieldSplitter, 3);
                    if (mapping.Length < 2)
                        throw new InvalidOperationException("Invalid confusable mapping line: " + line);

                    try
                    {
                        var confusableChar = uint.Parse(mapping[0].Trim(), NumberStyles.HexNumber);
                        var skeletonChars = mapping[1].Split(PairSplitter, StringSplitOptions.RemoveEmptyEntries).Select(l => uint.Parse(l, NumberStyles.HexNumber)).ToArray();
                        result.Add(confusableChar, skeletonChars);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException("Invalid confusable mapping line:" + line, e);
                    }
                }
            }
            if (result.Count == 0)
                throw new InvalidOperationException("Empty confusable mapping source");

            return result;
        }
    }
}
