using System.IO;

namespace CompatBot.Utils;

public static class PathUtils
{
    public static string[] GetSegments(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        var result = new List<string>();
        string segment;
        do
        {
            segment = Path.GetFileName(path);
            result.Add(segment is {Length: >0} ? segment : path);
            path = Path.GetDirectoryName(path);
        } while (segment is {Length: >0} && path is {Length: >0});
        result.Reverse();
        return result.ToArray();
    }
}