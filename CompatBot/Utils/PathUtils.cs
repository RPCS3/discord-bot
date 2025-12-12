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

    public static bool HasExecutableExtension(this string? filename)
    {
        if (filename is not { Length: > 0 })
            return false;
        
        return Path.GetExtension(filename).ToLower() switch
        {
            ".exe" or
            ".bat" or
            ".cmd" or
            ".ps1" or
            ".com" or
            ".vbs" or
            ".lnk" or
            ".url" or
            ".sh" => true,
            _ => false
        };
    }
}