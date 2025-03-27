using System.Collections.Specialized;
using System.Globalization;
using CompatBot.Utils.ResultFormatters;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Database.Providers;

internal static class HwInfoProvider
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly TimeSpan CacheTime = TimeSpan.FromDays(1);
    private static readonly MemoryCache UserCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

    public static async ValueTask AddOrUpdateSystemAsync(DiscordClient client, DiscordMessage msg, NameValueCollection items, CancellationToken cancellationToken)
    {
        var ignoreAuthor = await msg.Author.IsWhitelistedAsync(client, msg.Channel.Guild).ConfigureAwait(false);
        byte counter = 0;
        if (!ignoreAuthor
            && UserCache.TryGetValue(msg.Author.Id, out counter)
            && counter > 4)
        {
            Config.Log.Debug($"Ignoring HW report for user {msg.Author.Id} ({msg.Author.Username}#{msg.Author.Discriminator})");
            return;
        }

        if (items["cpu_model"] is not string cpuString
            || (items["gpu_name"] ?? items["gpu_info"]) is not string gpuString
            || !int.TryParse(items["thread_count"], out var threadCount)
            || !decimal.TryParse(items["memory_amount"], out var ramGB)
            || items["cpu_extensions"] is not string cpuExtensions)
            return;

        var cpuStringParts = cpuString.Split(' ', 2);
        var gpuStringParts = gpuString.Split(' ', 2);
        if (cpuStringParts.Length != 2 || gpuStringParts.Length != 2)
            return;

        if (cpuStringParts[0].ToLower() is not ("intel" or "amd" or "apple"))
        {
            Config.Log.Warn($"Unknown CPU maker {cpuStringParts[0]}, plz fix");
            return;
        }

        if (gpuStringParts[0].ToLower() is not ("nvidia" or "amd" or "ati" or "intel" or "apple"))
            if (LogParserResult.IsNvidia(gpuString))
                gpuStringParts = ["NVIDIA", gpuString];
            else if (LogParserResult.IsAmd(gpuString))
                gpuStringParts = ["AMD", gpuString];
            else
            {
                Config.Log.Warn($"Unknown GPU maker {gpuStringParts[0]}, plz fix");
                return;
            }

        var ts = msg.Timestamp.UtcDateTime;
        if (items["log_start_timestamp"] is string logTs
            && DateTime.TryParse(logTs, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var logTsVal))
            ts = logTsVal.ToUniversalTime();
        var osType = GetOsType(items["os_type"]);
        var info = new HwInfo
        {
            Timestamp = ts.Ticks,
            InstallId = GetHwId(items, msg),

            CpuMaker = cpuStringParts[0],
            CpuModel = cpuStringParts[1],
            ThreadCount = threadCount,
            CpuFeatures = GetFeatures(cpuExtensions),

            RamInMb = (int)(ramGB * 1024),

            GpuMaker = gpuStringParts[0],
            GpuModel = gpuStringParts[1],

            OsType = osType,
            OsName = GetName(osType, items),
            OsVersion = items["os_version"],
        };
        await using var db = await HardwareDb.OpenWriteAsync().ConfigureAwait(false);
        var existingItem = await db.HwInfo.FindAsync([info.InstallId], cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existingItem is null)
            db.HwInfo.Add(info);
        else if (existingItem.Timestamp <= info.Timestamp)
            db.Entry(existingItem).CurrentValues.SetValues(info);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to update hardware db");
        }
        if (!ignoreAuthor)
            UserCache.Set(msg.Author.Id, counter + 1, CacheTime);
    }

    private static byte[] GetHwId(NameValueCollection items, DiscordMessage message)
    {
        var id = items["hw_id"] ?? (message.Author.Id.ToString("x16") + items["compat_database_path"]);
        return Utf8.GetBytes(id).GetSaltedHash();
    }

    private static CpuFeatures GetFeatures(string extensions)
    {
        var result = CpuFeatures.None;
        foreach (var ext in extensions.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ext.StartsWith("AVX"))
            {
                result |= CpuFeatures.Avx;
                if (ext.EndsWith('x'))
                    result |= CpuFeatures.Xop;
                if (ext.Contains("512"))
                {
                    result |= CpuFeatures.Avx512;
                    if (ext.Contains('+'))
                        result |= CpuFeatures.Avx512IL;
                }
                else if (ext.Contains('+'))
                    result |= CpuFeatures.Avx2;
            }
            else if (ext.StartsWith("FMA"))
            {
                if (ext.Contains('3'))
                    result |= CpuFeatures.Fma3;
                if (ext.Contains('4'))
                    result |= CpuFeatures.Fma4;
            }
            else if (ext.StartsWith("TSX"))
            {
                result |= CpuFeatures.Tsx;
                if (ext.Contains("TSX-FA"))
                    result |= CpuFeatures.TsxFa;
            }
        }
        return result;
    }

    private static OsType GetOsType(string? osType)
        => osType switch
        {
            "Windows" => OsType.Windows,
            "Linux" => OsType.Linux,
            "MacOS" => OsType.MacOs,
            "" => OsType.Unknown,
            null => OsType.Unknown,
            _ => OsType.Bsd,
        };

    private static string? GetName(OsType osType, NameValueCollection items)
        => osType switch
        {
            OsType.Windows => items["os_windows_version"],
            OsType.Linux => items["os_linux_version"],
            OsType.MacOs => items["os_mac_version"],
            OsType.Bsd => items["os_linux_version"],
            _ => null
        };
}