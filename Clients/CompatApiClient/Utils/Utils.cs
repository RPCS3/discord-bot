using System;

namespace CompatApiClient.Utils;

public static class Utils
{
    private const long UnderKB = 1000;
    private const long UnderMB = 1000 * 1024;
    private const long UnderGB = 1000 * 1024 * 1024;

    public static string Trim(this string str, int maxLength)
    {
        if (str.Length > maxLength)
            return str[..(maxLength - 1)] + "…";

        return str;
    }

    public static string Truncate(this string str, int maxLength)
    {
        if (maxLength < 1)
            throw new ArgumentException("Argument must be positive, but was " + maxLength, nameof(maxLength));

        if (str.Length <= maxLength)
            return str;

        return str[..maxLength];
    }

    public static string Sanitize(this string str, bool breakLinks = true, bool replaceBackTicks = false)
    {
        var result = str.Replace("`", "`\u200d").Replace("@", "@\u200d");
        if (replaceBackTicks)
            result = result.Replace('`', '\'');
        if (breakLinks)
            result = result.Replace(".", ".\u200d").Replace(":", ":\u200d");
        return result;
    }

    public static int Clamp(this int amount, int low, int high)
        => Math.Min(high, Math.Max(amount, low));
        
    public static double Clamp(this double amount, double low, double high)
        => Math.Min(high, Math.Max(amount, low));

    public static string AsStorageUnit(this int bytes)
        => AsStorageUnit((long)bytes);

    public static string AsStorageUnit(this long bytes)
    {
        if (bytes < UnderKB)
            return $"{bytes} byte{(bytes == 1 ? "" : "s")}";
        if (bytes < UnderMB)
            return $"{bytes / 1024.0:0.##} KB";
        if (bytes < UnderGB)
            return $"{bytes / 1024.0 / 1024:0.##} MB";
        return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
    }
}