using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static class Hardware
{
    [Command("hardware")]
    [Description("Hardware survey data from uploaded log files")]
    public static async ValueTask Stats(
        SlashCommandContext ctx,
        [Description("Desired period in days, default is 30"), MinMaxValue(1)]
        int period = 30
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        var maxDays = DateTime.UtcNow - new DateTime(2011, 5, 23, 0, 0, 0, DateTimeKind.Utc);
        period = Math.Clamp(Math.Abs(period), 1, (int)maxDays.TotalDays);
        var ts = DateTime.UtcNow.AddDays(-period).Ticks;
        await using var db = new HardwareDb();
        var count = await db.HwInfo.AsNoTracking().CountAsync(i => i.Timestamp > ts).ConfigureAwait(false);
        if (count is 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} No data available for specified time period", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var top = Config.MaxPositionsForHwSurveyResults;
        var cpuMakers = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.CpuMaker)
            .Select(g => new { Count = g.Count(), Name = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);
        var gpuMakers= await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.GpuMaker)
            .Select(g => new { Count = g.Count(), Name = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);
        var osMakers= await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.OsType)
            .Select(g => new { Count = g.Count(), Type = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);
        
        var cpuModels = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.CpuModel)
            .Select(g => new { Count = g.Count(), Maker = g.First().CpuMaker, Name = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);
        var gpuModels = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.GpuModel)
            .Select(g => new { Count = g.Count(), Maker = g.First().GpuMaker, Name = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);
        var osModels = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.OsName)
            .Select(g => new { Count = g.Count(), Type = g.First().OsType, Name = g.Key })
            .OrderByDescending(r => r.Count)
            .Take(top)
            .ToListAsync()
            .ConfigureAwait(false);

        var cpuFeatures = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.CpuFeatures)
            .Select(g => new { Count = g.Count(), Features = g.Key })
            .ToListAsync()
            .ConfigureAwait(false);
        var featureStats = new Dictionary<CpuFeatures, int>();
        foreach (CpuFeatures feature in Enum.GetValues<CpuFeatures>())
        {
            if (feature == CpuFeatures.None)
                continue;
            
            var featureCount = cpuFeatures.Where(f => f.Features.HasFlag(feature)).Sum(f => f.Count);
            if (featureCount == 0)
                continue;
            
            featureStats[feature] = featureCount;
        }
        var sortedFeatureList = featureStats.OrderByDescending(kvp => kvp.Value).ThenByDescending(kvp => kvp.Key).Select(kvp => (Count: kvp.Value, Name: GetCpuFeature(kvp.Key))).ToList();

        var threads = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.ThreadCount)
            .Select(g => new { Count = g.Count(), Number = g.Key })
            .ToListAsync()
            .ConfigureAwait(false);
        var lowTc = threads.Where(i => i.Number < 4).Sum(i => i.Count);
        var highTc = threads.Where(i => i.Number > 12).Sum(i => i.Count);
        var threadStats = new (int Count, string Number)[] { (lowTc, "3 or fewer"), (highTc, "13 or more") }
            .Concat(threads.Where(i => i.Number is > 3 and < 13).Select(i => (i.Count, Number: i.Number.ToString())))
            .Where(i => i.Count > 0)
            .OrderByDescending(i => i.Count)
            .Take(top)
            .ToList();

        var mem = await db.HwInfo.AsNoTracking()
            .Where(i => i.Timestamp > ts)
            .GroupBy(i => i.RamInMb)
            .Select(g => new { Count = g.Count(), Mem = g.Key })
            .ToListAsync()
            .ConfigureAwait(false);
        const int margin = 200;
        var lowRam = mem.Where(i => i.Mem < 4 * 1024 - margin).Sum(i => i.Count);
        var ram4to6 = mem.Where(i => i.Mem is >= 4 * 1024 - margin and < 6 * 1024 - margin).Sum(i => i.Count);
        var ram6to8 = mem.Where(i => i.Mem is >= 6 * 1024 - margin and < 8 * 1024 - margin).Sum(i => i.Count);
        var ram8to16 = mem.Where(i => i.Mem is >= 8 * 1024 - margin and < 16 * 1024 - margin).Sum(i => i.Count);
        var ram16to32 = mem.Where(i => i.Mem is >= 16 * 1024 - margin and < 32 * 1024 - margin).Sum(i => i.Count);
        var ram32to48 = mem.Where(i => i.Mem is >= 32 * 1024 - margin and < 48 * 1024 - margin).Sum(i => i.Count);
        var highRam = mem.Where(i => i.Mem >= 48 * 1024 - margin).Sum(i => i.Count);
        var ramStats = new (int Count, string Mem)[]
            {
                (lowRam, "less than 4 GB"),
                (ram4to6, "4 to 6 GB"),
                (ram6to8, "6 to 8 GB"),
                (ram8to16, "8 to 16 GB"),
                (ram16to32, "16 to 32 GB"),
                (ram32to48, "32 to 48 GB"),
                (highRam, "48 GB or more"),
            }
            .Where(i => i.Count > 0)
            //.Reverse()
            .OrderByDescending(i => i.Count)
            .Take(top)
            .ToList();
        
        var embed = new DiscordEmbedBuilder()
            .WithTitle($"RPCS3 Hardware Survey (past {period} day{(period == 1 ? "" : "s")})")
            .WithDescription($"Statistics from the {count} most recent system configuration{(count == 1 ? "" : "s")} found in uploaded RPCS3 logs.")

            .AddField("Top CPU Makers", string.Join('\n', cpuMakers.Select((m, n) => $"{GetNum(n)} {m.Name} ({m.Count * 100.0 / count:0.##}%)")), true)
            .AddField("Top GPU Makers", string.Join('\n', gpuMakers.Select((m, n) => $"{GetNum(n)} {m.Name} ({m.Count * 100.0 / count:0.##}%)")), true)
            .AddField("Top OS Types", string.Join('\n', osMakers.Select((m, n) => $"{GetNum(n)} {GetOsType(m.Type)} ({m.Count * 100.0 / count:0.##}%)")), true)

            .AddField("Top CPU Models", string.Join('\n', cpuModels.Select((m, n) => $"{GetNum(n)} {m.Maker} {m.Name} ({m.Count * 100.0 / count:0.##}%)")), true)
            .AddField("Top GPU Models", string.Join('\n', gpuModels.Select((m, n) => $"{GetNum(n)} {m.Maker} {m.Name} ({m.Count * 100.0 / count:0.##}%)")), true)
            .AddField("Top OS Versions", string.Join('\n', osModels.Select((m, n) => $"{GetNum(n)} {(m.Type == OsType.Windows ? "Windows " : "")}{m.Name} ({m.Count * 100.0 / count:0.##}%)")), true)

            .AddField("Top AVX Extensions",
                string.Join('\n', sortedFeatureList.Where(i => i.Name.StartsWith("AVX")).Select((i, n) => $"{i.Count * 100.0 / count:0.00}% {i.Name}")) is { Length: > 0 } avx ? avx : "No Data",
                true)
            .AddField("Top FMA Extensions",
                string.Join('\n', sortedFeatureList.Where(i => i.Name.StartsWith("FMA") || i.Name.StartsWith("XOP")).Select((i, n) => $"{i.Count * 100.0 / count:0.00}% {i.Name}")) is { Length: > 0 } fma ? fma : "No Data",
                true)
            .AddField("Top TSX Extensions", 
                string.Join('\n', sortedFeatureList.Where(i => i.Name.StartsWith("TSX")).Select((i, n) => $"{i.Count * 100.0 / count:0.00}% {i.Name}")) is { Length: > 0 } tsx ? tsx : "No Data",
                true)
            
            .AddField("Top Thread Configurations", string.Join('\n', threadStats.Select(i => $"{i.Count*100.0/count:0.00}% {i.Number} threads")), true)
            .AddField("Top RAM Configurations", string.Join('\n', ramStats.Select(i => $"{i.Count*100.0/count:0.00}% {i.Mem}")), true)

            .WithFooter("All collected data is anonymous, for details see bot source code");
        await ctx.RespondAsync(embed: embed, ephemeral: ephemeral).ConfigureAwait(false);
    }

    private static string GetNum(int position)
        => position switch
        {
            0 => "🏆",
            1 => "🥈",
            2 => "🥉",
            3 => "4️⃣",
            4 => "5️⃣",
            5 => "6️⃣",
            6 => "7️⃣",
            7 => "8️⃣",
            8 => "9️⃣",
            _ => $"{position + 1}."
        };

    private static string GetOsType(OsType type)
        => type switch
        {
            OsType.MacOs => "macOS",
            OsType.Bsd => "BSD",
            _ => type.ToString()
        };

    private static string GetCpuFeature(CpuFeatures feature)
        => feature switch
        {
            CpuFeatures.Avx => "AVX",
            CpuFeatures.Avx2 => "AVX2",
            CpuFeatures.Avx512 => "AVX-512",
            CpuFeatures.Avx512IL => "AVX-512+",
            CpuFeatures.Fma3 => "FMA3",
            CpuFeatures.Fma4 => "FMA4",
            CpuFeatures.Xop => "XOP",
            CpuFeatures.Tsx => "TSX",
            CpuFeatures.TsxFa => "TSX-FA",
            CpuFeatures.None => "",
            _ => throw new ArgumentException($"Unknown CPU Feature {feature}", nameof(feature)),
        };
}