using System;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace CompatApiClient.Utils;

public static class Utils
{
    private const long UnderKB = 1000;
    private const long UnderMB = 1000 * 1024;
    private const long UnderGB = 1000 * 1024 * 1024;

    public static string Trim(this string? str, int maxLength)
    {
        if (str is null)
            return "";
        
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

    public static string RemoveWhitespaces(this string str)
    {
        var result = new StringBuilder();
        foreach (var rune in str.EnumerateRunes().Where(r => !Rune.IsWhiteSpace(r)))
            result.Append(rune.ToString());
        return result.ToString();
    }

    public static int Clamp(this int amount, int low, int high)
        => Math.Min(high, Math.Max(amount, low));
        
    public static double Clamp(this double amount, double low, double high)
        => Math.Min(high, Math.Max(amount, low));

    public static string AsStorageUnit(this int bytes)
        => AsStorageUnit((long)bytes);

    public static string AsStorageUnit(this long bytes)
        => bytes switch
        {
            < UnderKB => $"{bytes} byte{(bytes == 1 ? "" : "s")}",
            < UnderMB => $"{bytes / 1024.0:0.##} KiB",
            < UnderGB => $"{bytes / (1024.0 * 1024):0.##} MiB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GiB"
        };

    public static HttpClient WithUserAgent(this HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.Add(ApiConfig.ProductInfoHeader);
        return client;
    }

    public static HttpClient WithTimeout(this HttpClient client, TimeSpan timeout)
    {
        client.Timeout = timeout;
        return client;
    }
}