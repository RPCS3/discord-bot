using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Commands
{
    [Group("stats")]
    internal sealed class BotStats: BaseCommandModuleCustom
    {
        [GroupCommand, Cooldown(1, 10, CooldownBucketType.Global)]
        [Description("Use to look at various runtime stats")]
        public async Task Show(CommandContext ctx)
        {
            var embed = new DiscordEmbedBuilder
            {
                Color = DiscordColor.Purple,
            }
                        .AddField("Current uptime", Config.Uptime.Elapsed.AsShortTimespan(), true)
                        .AddField("Discord latency", $"{ctx.Client.Ping} ms", true)
                        .AddField("Memory Usage", $"{GC.GetTotalMemory(false).AsStorageUnit()}", true)
                        .AddField("Google Drive API", File.Exists(Config.GoogleApiConfigPath) ? "✅ Configured" : "❌ Not configured", true)
                        .AddField("GitHub rate limit", $"{GithubClient.Client.RateLimitRemaining} out of {GithubClient.Client.RateLimit} calls available\nReset in {(GithubClient.Client.RateLimitResetTime - DateTime.UtcNow).AsShortTimespan()}", true)
                        .AddField(".NET versions", $"Runtime {System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion()}\n" +
                                                   $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
                                                   //$"SIMD Acceleration:{(Vector.IsHardwareAccelerated ? "✅ Supported" : "❌ Not supported")}\n" +
                                                   $"Confinement: {SandboxDetector.Detect() ?? "None"}", true);
            AppendPiracyStats(embed);
            AppendCmdStats(ctx, embed);
            AppendExplainStats(embed);
            AppendGameLookupStats(embed);
            AppendSyscallsStats(embed);
#if DEBUG
            embed.WithFooter("Test Instance");
#endif
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }

        private static void AppendPiracyStats(DiscordEmbedBuilder embed)
        {
            try
            {
                using (var db = new BotDb())
                {
                    var timestamps = db.Warning
                        .Where(w => w.Timestamp.HasValue && !w.Retracted)
                        .OrderBy(w => w.Timestamp)
                        .Select(w => w.Timestamp.Value)
                        .ToList();
                    var firstWarnTimestamp = timestamps.FirstOrDefault();
                    var previousTimestamp = firstWarnTimestamp;
                    var longestGapBetweenWarning = 0L;
                    long longestGapStart = 0L, longestGapEnd = 0L;
                    var span24h = TimeSpan.FromHours(24).Ticks;
                    var currentSpan = new Queue<long>();
                    long mostWarningsStart = 0L, mostWarningsEnd = 0L, daysWithoutWarnings = 0L;
                    var mostWarnings = 0;
                    for (var i = 0; i < timestamps.Count; i++)
                    {
                        var currentTimestamp = timestamps[i];
                        var newGap = currentTimestamp - previousTimestamp;
                        if (newGap > longestGapBetweenWarning)
                        {
                            longestGapBetweenWarning = newGap;
                            longestGapStart = previousTimestamp;
                            longestGapEnd = currentTimestamp;
                        }
                        if (newGap > span24h)
                            daysWithoutWarnings += newGap / span24h;
                        previousTimestamp = currentTimestamp;

                        currentSpan.Enqueue(currentTimestamp);
                        while (currentSpan.Count > 0 && currentTimestamp - currentSpan.Peek() > span24h)
                            currentSpan.Dequeue();
                        if (currentSpan.Count > mostWarnings)
                        {
                            mostWarnings = currentSpan.Count;
                            mostWarningsStart = currentSpan.Peek();
                            mostWarningsEnd = currentTimestamp;
                        }
                    }
                    var yesterday = DateTime.UtcNow.AddDays(-1).Ticks;
                    var warnCount = db.Warning.Count(w => w.Timestamp > yesterday);
                    var lastWarn = db.Warning.LastOrDefault()?.Timestamp;
                    if (lastWarn.HasValue)
                        longestGapBetweenWarning = Math.Max(longestGapBetweenWarning, DateTime.UtcNow.Ticks - lastWarn.Value);
                    // most warnings per 24h
                    var statsBuilder = new StringBuilder();
                    if (longestGapBetweenWarning > 0)
                        statsBuilder.AppendLine($@"Longest between warnings: **{TimeSpan.FromTicks(longestGapBetweenWarning).AsShortTimespan()}** between {longestGapStart.AsUtc():yyyy-MM-dd} and {longestGapEnd.AsUtc():yyyy-MM-dd}");
                    if (mostWarnings > 0)
                        statsBuilder.AppendLine($"Most warnings in 24h: **{mostWarnings}** on {mostWarningsEnd.AsUtc():yyyy-MM-dd}");
                    if (daysWithoutWarnings > 0 && firstWarnTimestamp > 0)
                        statsBuilder.AppendLine($"Full days without warnings: **{daysWithoutWarnings}** out of {(DateTime.UtcNow - firstWarnTimestamp.AsUtc()).TotalDays:0}");
                    {
                        statsBuilder.Append($"Warnings in the last 24h: **{warnCount}**");
                        if (warnCount == 0)
                            statsBuilder.Append(" ").Append(BotReactionsHandler.RandomPositiveReaction);
                        statsBuilder.AppendLine();
                    }
                    if (lastWarn.HasValue)
                        statsBuilder.AppendLine($@"Time since last warning: {(DateTime.UtcNow - lastWarn.Value.AsUtc()).AsShortTimespan()}");
                    embed.AddField("Warning stats", statsBuilder.ToString().TrimEnd(), true);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        private static void AppendCmdStats(CommandContext ctx, DiscordEmbedBuilder embed)
        {
            var commandStats = StatsStorage.CmdStatCache.GetCacheKeys<string>();
            var sortedCommandStats = commandStats
                .Select(c => (name: c, stat: StatsStorage.CmdStatCache.Get(c) as int?))
                .Where(c => c.stat.HasValue)
                .OrderByDescending(c => c.stat)
                .ToList();
            var totalCalls = sortedCommandStats.Sum(c => c.stat);
            var top = sortedCommandStats.Take(5).ToList();
            if (top.Any())
            {
                var statsBuilder = new StringBuilder();
                var n = 1;
                foreach (var cmdStat in top)
                    statsBuilder.AppendLine($"{n++}. {cmdStat.name} ({cmdStat.stat} call{(cmdStat.stat == 1 ? "" : "s")}, {cmdStat.stat * 100.0 / totalCalls:0.##}%)");
                statsBuilder.AppendLine($"Total commands executed: {totalCalls}");
                embed.AddField($"Top {top.Count} recent commands", statsBuilder.ToString().TrimEnd(), true);
            }
        }

        private static void AppendExplainStats(DiscordEmbedBuilder embed)
        {
            var terms = StatsStorage.ExplainStatCache.GetCacheKeys<string>();
            var sortedTerms = terms
                .Select(t => (term: t, stat: StatsStorage.ExplainStatCache.Get(t) as int?))
                .Where(t => t.stat.HasValue)
                .OrderByDescending(t => t.stat)
                .ToList();
            var totalExplains = sortedTerms.Sum(t => t.stat);
            var top = sortedTerms.Take(5).ToList();
            if (top.Any())
            {
                var statsBuilder = new StringBuilder();
                var n = 1;
                foreach (var explain in top)
                    statsBuilder.AppendLine($"{n++}. {explain.term} ({explain.stat} display{(explain.stat == 1 ? "" : "s")}, {explain.stat * 100.0 / totalExplains:0.##}%)");
                statsBuilder.AppendLine($"Total explanations shown: {totalExplains}");
                embed.AddField($"Top {top.Count} recent explanations", statsBuilder.ToString().TrimEnd(), true);
            }
        }

        private static void AppendGameLookupStats(DiscordEmbedBuilder embed)
        {
            var gameTitles = StatsStorage.GameStatCache.GetCacheKeys<string>();
            var sortedTitles = gameTitles
                .Select(t => (title: t, stat: StatsStorage.GameStatCache.Get(t) as int?))
                .Where(t => t.stat.HasValue)
                .OrderByDescending(t => t.stat)
                .ToList();
            var totalLookups = sortedTitles.Sum(t => t.stat);
            var top = sortedTitles.Take(5).ToList();
            if (top.Any())
            {
                var statsBuilder = new StringBuilder();
                var n = 1;
                foreach (var title in top)
                    statsBuilder.AppendLine($"{n++}. {title.title.Trim(40)} ({title.stat} search{(title.stat == 1 ? "" : "es")}, {title.stat * 100.0 / totalLookups:0.##}%)");
                statsBuilder.AppendLine($"Total game lookups: {totalLookups}");
                embed.AddField($"Top {top.Count} recent game lookups", statsBuilder.ToString().TrimEnd(), true);
            }
        }

        private void AppendSyscallsStats(DiscordEmbedBuilder embed)
        {
            using (var db = new ThumbnailDb())
            {
                var syscallCount = db.SyscallInfo.Where(sci => sci.Function.StartsWith("sys_")).Distinct().Count();
                var syscallModuleCount = db.SyscallInfo.Where(sci => sci.Function.StartsWith("sys_")).Select(sci => sci.Module).Distinct().Count();
                var totalFuncCount = db.SyscallInfo.Select(sci => sci.Function).Distinct().Count();
                var totalModuleCount = db.SyscallInfo.Select(sci => sci.Module).Distinct().Count();
                var fwCallCount = totalFuncCount - syscallCount;
                var fwModuleCount = totalModuleCount - syscallModuleCount;
                var gameCount = db.SyscallToProductMap.Select(m => m.ProductId).Distinct().Count();
                embed.AddField("SceCall Stats",
                    $"Tracked game IDs: {gameCount}\n" +
                    $"Tracked syscalls: {syscallCount} function{(syscallCount == 1 ? "" : "s")} in {syscallModuleCount} module{(syscallModuleCount == 1 ? "" : "s")}\n" +
                    $"Tracked fw calls: {fwCallCount} function{(fwCallCount == 1 ? "" : "s")} in {fwModuleCount} module{(fwModuleCount == 1 ? "" : "s")}\n",
                    true);
            }
        }
    }
}
