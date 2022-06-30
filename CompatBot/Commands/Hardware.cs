using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Group("hardware"), Aliases("hw")]
[Description("Various hardware stats from uploaded log files")]
internal sealed class Hardware: BaseCommandModuleCustom
{
    [GroupCommand]
    public Task Show(CommandContext ctx) => ShowStats(ctx);

    [Command("stats")]
    public Task Stats(CommandContext ctx, [Description("Desired period in days, default is 30")] int period = 30) => ShowStats(ctx, period);
    
    public static async Task ShowStats(CommandContext ctx, [Description("Desired period in days, default is 30")] int period = 30)
    {
        var maxDays = DateTime.UtcNow - new DateTime(2011, 5, 23, 0, 0, 0, DateTimeKind.Utc);
        period = Math.Clamp(Math.Abs(period), 0, (int)maxDays.TotalDays);
        var ts = DateTime.UtcNow.AddDays(-period).Ticks;
        await using var db = new HardwareDb();
        var count = await db.HwInfo.AsNoTracking().CountAsync(i => i.Timestamp > ts).ConfigureAwait(false);
        if (count == 0)
        {
            await ctx.RespondAsync("No data available for specified time period").ConfigureAwait(false);
            return;
        }

        const int top = 10;
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
        foreach (CpuFeatures feature in Enum.GetValues(typeof(CpuFeatures)))
        {
            if (feature == CpuFeatures.None)
                continue;
            
            var featureCount = cpuFeatures.Where(f => f.Features.HasFlag(feature)).Select(f => f.Count).Sum();
            if (featureCount == 0)
                continue;
            
            featureStats[feature] = featureCount;
        }
        var sortedFeatureList = featureStats.OrderByDescending(kvp => kvp.Value).ThenByDescending(kvp => kvp.Key).Select(kvp => (Count: kvp.Value, Name: GetCpuFeature(kvp.Key))).ToList();

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

            .WithFooter("All collected data is anonymous, for details see bot source code");
        await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
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
            CpuFeatures.Avx512IL => "AVX-512IL",
            CpuFeatures.Fma3 => "FMA3",
            CpuFeatures.Fma4 => "FMA4",
            CpuFeatures.Xop => "XOP",
            CpuFeatures.Tsx => "TSX",
            CpuFeatures.TsxFa => "TSX-FA",
            CpuFeatures.None => "",
            _ => throw new ArgumentException($"Unknown CPU Feature {feature}", nameof(feature)),
        };
}