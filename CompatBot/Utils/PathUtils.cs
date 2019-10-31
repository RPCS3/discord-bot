using System.Collections.Generic;
using System.IO;

namespace CompatBot.Utils
{
    public static class PathUtils
    {
        public static string[] GetSegments(string path)
        {
            if (string.IsNullOrEmpty(path))
                return new string[0];

            var result = new List<string>();
            string segment;
            do
            {
                segment = Path.GetFileName(path);
                result.Add(string.IsNullOrEmpty(segment) ? path : segment);
                path = Path.GetDirectoryName(path);
            } while (!string.IsNullOrEmpty(segment) && !string.IsNullOrEmpty(path));
            result.Reverse();
            return result.ToArray();
        }
    }
}
