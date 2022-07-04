using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Group("stats")]
internal sealed class BotStats: BaseCommandModuleCustom
{
    [GroupCommand]
    [Description("Use to look at various runtime stats")]
    public async Task Show(CommandContext ctx)
    {
        var embed = new DiscordEmbedBuilder
            {
                Color = DiscordColor.Purple,
            }
            .AddField("Current Uptime", Config.Uptime.Elapsed.AsShortTimespan(), true)
            .AddField("Discord Latency", $"{ctx.Client.Ping} ms", true);
        if (!string.IsNullOrEmpty(Config.AzureComputerVisionKey))
            embed.AddField("Max OCR Queue", MediaScreenshotMonitor.MaxQueueLength.ToString(), true);
        var osInfo = RuntimeInformation.OSDescription;
        if (Environment.OSVersion.Platform is PlatformID.Unix or PlatformID.MacOSX)
            osInfo = RuntimeInformation.RuntimeIdentifier;
        embed.AddField("API Tokens", GetConfiguredApiStats(), true)
            .AddField("Memory Usage", $"GC: {GC.GetGCMemoryInfo().HeapSizeBytes.AsStorageUnit()}/{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes.AsStorageUnit()}\n" +
                                      $"API pools: L: {ApiConfig.MemoryStreamManager.LargePoolInUseSize.AsStorageUnit()}/{(ApiConfig.MemoryStreamManager.LargePoolInUseSize + ApiConfig.MemoryStreamManager.LargePoolFreeSize).AsStorageUnit()}" +
                                      $" S: {ApiConfig.MemoryStreamManager.SmallPoolInUseSize.AsStorageUnit()}/{(ApiConfig.MemoryStreamManager.SmallPoolInUseSize + ApiConfig.MemoryStreamManager.SmallPoolFreeSize).AsStorageUnit()}\n" +
                                      $"Bot pools: L: {Config.MemoryStreamManager.LargePoolInUseSize.AsStorageUnit()}/{(Config.MemoryStreamManager.LargePoolInUseSize + Config.MemoryStreamManager.LargePoolFreeSize).AsStorageUnit()}" +
                                      $" S: {Config.MemoryStreamManager.SmallPoolInUseSize.AsStorageUnit()}/{(Config.MemoryStreamManager.SmallPoolInUseSize + Config.MemoryStreamManager.SmallPoolFreeSize).AsStorageUnit()}", true)
            .AddField("GitHub Rate Limit", $"{GithubClient.Client.RateLimitRemaining} out of {GithubClient.Client.RateLimit} calls available\nReset in {(GithubClient.Client.RateLimitResetTime - DateTime.UtcNow).AsShortTimespan()}", true)
            .AddField(".NET Info", $"{RuntimeInformation.FrameworkDescription}\n" +
                                   $"{(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")} GC Mode", true)
            .AddField("Runtime Info", $"Confinement: {SandboxDetector.Detect()}\n" +
                                      $"OS: {osInfo}\n" +
                                      $"CPUs: {Environment.ProcessorCount}\n" +
                                      $"Time zones: {TimeParser.TimeZoneMap.Count} out of {TimeParser.TimeZoneAcronyms.Count} resolved, {TimeZoneInfo.GetSystemTimeZones().Count} total", true);
        AppendPiracyStats(embed);
        AppendCmdStats(embed);
        AppendExplainStats(embed);
        AppendGameLookupStats(embed);
        AppendSyscallsStats(embed);
        AppendHwInfoStats(embed);
        AppendPawStats(embed);
#if DEBUG
        embed.WithFooter("Test Instance");
#endif
        var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
        await ch.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    [Command("hw"), Aliases("hardware")]
    [Description("Various hardware stats from uploaded log files")]
    [Cooldown(1, 5, CooldownBucketType.Guild)]
    public Task Hardware(CommandContext ctx, [Description("Desired period in days, default is 30")] int period = 30) => Commands.Hardware.ShowStats(ctx, period);
    
    private static string GetConfiguredApiStats()
    {
        return new StringBuilder()
            .Append(File.Exists(Config.GoogleApiConfigPath) ? "✅" : "❌").AppendLine(" Google Drive")
            .Append(string.IsNullOrEmpty(Config.AzureDevOpsToken) ? "❌" : "✅").AppendLine(" Azure DevOps")
            .Append(string.IsNullOrEmpty(Config.AzureComputerVisionKey) ? "❌" : "✅").AppendLine(" Computer Vision")
            .Append(string.IsNullOrEmpty(Config.AzureAppInsightsKey) ? "❌" : "✅").AppendLine(" AppInsights")
            .Append(string.IsNullOrEmpty(Config.GithubToken) ? "❌" : "✅").AppendLine(" Github")
            .ToString()
            .Trim();

    }

    private static void AppendPiracyStats(DiscordEmbedBuilder embed)
    {
        try
        {
            using var db = new BotDb();
            var timestamps = db.Warning
                .Where(w => w.Timestamp.HasValue && !w.Retracted)
                .OrderBy(w => w.Timestamp)
                .Select(w => w.Timestamp!.Value)
                .ToList();
            var firstWarnTimestamp = timestamps.FirstOrDefault();
            var previousTimestamp = firstWarnTimestamp;
            var longestGapBetweenWarning = 0L;
            long longestGapStart = 0L, longestGapEnd = 0L;
            var span24H = TimeSpan.FromHours(24).Ticks;
            var currentSpan = new Queue<long>();
            long mostWarningsEnd = 0L, daysWithoutWarnings = 0L;
            var mostWarnings = 0;
            for (var i = 1; i < timestamps.Count; i++)
            {
                var currentTimestamp = timestamps[i];
                var newGap = currentTimestamp - previousTimestamp;
                if (newGap > longestGapBetweenWarning)
                {
                    longestGapBetweenWarning = newGap;
                    longestGapStart = previousTimestamp;
                    longestGapEnd = currentTimestamp;
                }
                if (newGap > span24H)
                    daysWithoutWarnings += newGap / span24H;

                currentSpan.Enqueue(currentTimestamp);
                while (currentSpan.Count > 0 && currentTimestamp - currentSpan.Peek() > span24H)
                    currentSpan.Dequeue();
                if (currentSpan.Count > mostWarnings)
                {
                    mostWarnings = currentSpan.Count;
                    currentSpan.Peek();
                    mostWarningsEnd = currentTimestamp;
                }
                previousTimestamp = currentTimestamp;
            }

            var utcNow = DateTime.UtcNow;
            var yesterday = utcNow.AddDays(-1).Ticks;
            var last24HWarnings = db.Warning.Where(w => w.Timestamp > yesterday && !w.Retracted).ToList();
            var warnCount = last24HWarnings.Count;
            if (warnCount > mostWarnings)
            {
                mostWarnings = warnCount;
                mostWarningsEnd = utcNow.Ticks;
            }
            var lastWarn = timestamps.Any() ? timestamps.Last() : (long?)null;
            if (lastWarn.HasValue)
            {
                var currentGapBetweenWarnings = utcNow.Ticks - lastWarn.Value;
                if (currentGapBetweenWarnings > longestGapBetweenWarning)
                {
                    longestGapBetweenWarning = currentGapBetweenWarnings;
                    longestGapStart = lastWarn.Value;
                    longestGapEnd = utcNow.Ticks;
                }
                daysWithoutWarnings += currentGapBetweenWarnings / span24H;
            }
            // most warnings per 24h
            var statsBuilder = new StringBuilder();
            var rightDate = longestGapEnd == utcNow.Ticks ? "now" : longestGapEnd.AsUtc().ToString("yyyy-MM-dd");
            if (longestGapBetweenWarning > 0)
                statsBuilder.AppendLine($"Longest between warnings: **{TimeSpan.FromTicks(longestGapBetweenWarning).AsShortTimespan()}** between {longestGapStart.AsUtc():yyyy-MM-dd} and {rightDate}");
            rightDate = mostWarningsEnd == utcNow.Ticks ? "today" : $"on {mostWarningsEnd.AsUtc():yyyy-MM-dd}";
            if (mostWarnings > 0)
                statsBuilder.AppendLine($"Most warnings in 24h: **{mostWarnings}** {rightDate}");
            if (daysWithoutWarnings > 0 && firstWarnTimestamp > 0)
                statsBuilder.AppendLine($"Full days without warnings: **{daysWithoutWarnings}** out of {(DateTime.UtcNow - firstWarnTimestamp.AsUtc()).TotalDays:0}");
            {
                statsBuilder.Append($"Warnings in the last 24h: **{warnCount}**");
                if (warnCount == 0)
                    statsBuilder.Append(' ').Append(BotReactionsHandler.RandomPositiveReaction);
                statsBuilder.AppendLine();
            }
            if (lastWarn.HasValue)
                statsBuilder.AppendLine($"Time since last warning: {(DateTime.UtcNow - lastWarn.Value.AsUtc()).AsShortTimespan()}");
            embed.AddField("Warning Stats", statsBuilder.ToString().TrimEnd(), true);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    private static void AppendCmdStats(DiscordEmbedBuilder embed)
    {
        var sortedCommandStats = StatsStorage.GetCmdStats();
        var totalCalls = sortedCommandStats.Sum(c => c.stat);
        var top = sortedCommandStats.Take(5).ToList();
        if (top.Count == 0)
            return;
            
        var statsBuilder = new StringBuilder();
        var n = 1;
        foreach (var (name, stat) in top)
            statsBuilder.AppendLine($"{n++}. {name} ({stat} call{(stat == 1 ? "" : "s")}, {stat * 100.0 / totalCalls:0.##}%)");
        statsBuilder.AppendLine($"Total commands executed: {totalCalls}");
        embed.AddField($"Top {top.Count} Recent Commands", statsBuilder.ToString().TrimEnd(), true);
    }

    private static void AppendExplainStats(DiscordEmbedBuilder embed)
    {
        var sortedTerms = StatsStorage.GetExplainStats();
        var totalExplains = sortedTerms.Sum(t => t.stat);
        var top = sortedTerms.Take(5).ToList();
        if (top.Count == 0)
            return;
            
        var statsBuilder = new StringBuilder();
        var n = 1;
        foreach (var (term, stat) in top)
            statsBuilder.AppendLine($"{n++}. {term} ({stat} display{(stat == 1 ? "" : "s")}, {stat * 100.0 / totalExplains:0.##}%)");
        statsBuilder.AppendLine($"Total explanations shown: {totalExplains}");
        embed.AddField($"Top {top.Count} Recent Explanations", statsBuilder.ToString().TrimEnd(), true);
    }

    private static void AppendGameLookupStats(DiscordEmbedBuilder embed)
    {
        var sortedTitles = StatsStorage.GetGameStats();
        var totalLookups = sortedTitles.Sum(t => t.stat);
        var top = sortedTitles.Take(5).ToList();
        if (top.Count == 0)
            return;
            
        var statsBuilder = new StringBuilder();
        var n = 1;
        foreach (var (title, stat) in top)
            statsBuilder.AppendLine($"{n++}. {title.Trim(40)} ({stat} search{(stat == 1 ? "" : "es")}, {stat * 100.0 / totalLookups:0.##}%)");
        statsBuilder.AppendLine($"Total game lookups: {totalLookups}");
        embed.AddField($"Top {top.Count} Recent Game Lookups", statsBuilder.ToString().TrimEnd(), true);
    }

    private static void AppendSyscallsStats(DiscordEmbedBuilder embed)
    {
        try
        {
            using var db = new ThumbnailDb();
            var syscallCount = db.SyscallInfo.AsNoTracking().Where(sci => sci.Function.StartsWith("sys_") || sci.Function.StartsWith("_sys_")).Distinct().Count();
            var totalFuncCount = db.SyscallInfo.AsNoTracking().Select(sci => sci.Function).Distinct().Count();
            var fwCallCount = totalFuncCount - syscallCount;
            var gameCount = db.SyscallToProductMap.AsNoTracking().Select(m => m.ProductId).Distinct().Count();
            embed.AddField("SceCall Stats",
                $"Tracked game IDs: {gameCount}\n" +
                $"Tracked syscalls: {syscallCount} function{(syscallCount == 1 ? "" : "s")}\n" +
                $"Tracked fw calls: {fwCallCount} function{(fwCallCount == 1 ? "" : "s")}\n",
                true);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    private static void AppendHwInfoStats(DiscordEmbedBuilder embed)
    {
        try
        {
            using var db = new HardwareDb();
            var monthAgo = DateTime.UtcNow.AddDays(-30).Ticks;
            var monthCount = db.HwInfo.Count(i => i.Timestamp > monthAgo);
            if (monthCount == 0)
                return;
            
            var totalCount = db.HwInfo.Count();
            var cpu = db.HwInfo.AsNoTracking()
                .Where(i => i.Timestamp > monthAgo)
                .GroupBy(i => i.CpuModel)
                .Select(g => new { count = g.Count(), name = g.Key, maker = g.First().CpuMaker })
                .OrderByDescending(s => s.count)
                .FirstOrDefault();

            var cpuInfo = "";
            if (cpu is not null)
                cpuInfo = $"\nPopular CPU: {cpu.maker} {cpu.name} ({cpu.count*100.0/monthCount:0.##}%)";
            embed.AddField("Hardware Stats",
                $"Total: {totalCount} system{(totalCount == 1 ? "" : "s")}\n" +
                $"Last 30 days: {monthCount} system{(monthCount == 1 ? "" : "s")}" +
                cpuInfo,
                true);

        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    private static void AppendPawStats(DiscordEmbedBuilder embed)
    {
        try
        {
            using var db = new BotDb();
            var kots = db.Kot.Count();
            var doggos = db.Doggo.Count();
            if (kots == 0 && doggos == 0)
                return;

            var diff = kots > doggos ? (double)kots / doggos - 1.0 : (double)doggos / kots - 1.0;
            var sign = double.IsNaN(diff) || (double.IsFinite(diff) && !double.IsNegative(diff) && diff < 0.05) ? ":" : (kots > doggos ? ">" : "<");
            var kot = sign switch
            {
                ">" => GoodKot[new Random().Next(GoodKot.Length)],
                ":" => "🐱",
                _ => MeanKot[new Random().Next(MeanKot.Length)]
            };
            embed.AddField("🐾 Stats", $"{kot} {kots - 1} {sign} {doggos - 1} 🐶", true);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    internal static readonly string[] GoodDog = {"🐶", "🐕", "🐩", "🐕‍🦺",};
    internal static readonly string[] GoodKot = {"😸", "😺", "😻", "😽",};
    private static readonly string[] MeanKot = {"🙀", "😿", "😾",};
}