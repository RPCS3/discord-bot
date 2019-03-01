using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
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
                        .AddField("GitHub rate limit", $"{GithubClient.Client.RateLimitRemaining} out of {GithubClient.Client.RateLimit} calls available\nReset in {(GithubClient.Client.RateLimitResetTime - DateTime.UtcNow).AsShortTimespan()}", true)
                        .AddField("Google Drive API", File.Exists(GoogleDriveHandler.CredsPath) ? "✅ Configured" : "❌ Not configured")
                        .AddField(".NET versions", $"Runtime {System.Runtime.InteropServices.RuntimeEnvironment.GetSystemVersion()}\n{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}", true);
            AppendPiracyStats(embed);
            AppendCmdStats(ctx, embed);
            AppendExplainStats(embed);
            AppendGameLookupStats(embed);
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendMessageAsync(embed: embed).ConfigureAwait(false);
        }

        private static void AppendPiracyStats(DiscordEmbedBuilder embed)
        {
            try
            {
                using (var db = new BotDb())
                {
                    var longestGapBetweenWarning = db.Warning
                        .Where(w => w.Timestamp.HasValue)
                        .OrderBy(w => w.Timestamp)
                        .Pairwise((l, r) => r.Timestamp - l.Timestamp)
                        .Max();
                    var yesterday = DateTime.UtcNow.AddDays(-1).Ticks;
                    var warnCount = db.Warning.Count(w => w.Timestamp > yesterday);
                    var lastWarn = db.Warning.LastOrDefault()?.Timestamp;
                    if (lastWarn.HasValue && longestGapBetweenWarning.HasValue)
                        longestGapBetweenWarning = Math.Max(longestGapBetweenWarning.Value, DateTime.UtcNow.Ticks - lastWarn.Value);
                    var statsBuilder = new StringBuilder();
                    if (longestGapBetweenWarning.HasValue)
                        statsBuilder.AppendLine($@"Longest between warnings: {TimeSpan.FromTicks(longestGapBetweenWarning.Value).AsShortTimespan()}");
                    if (lastWarn.HasValue)
                        statsBuilder.AppendLine($@"Time since last warning: {(DateTime.UtcNow - lastWarn.Value.AsUtc()).AsShortTimespan()}");
                    statsBuilder.Append($"Warnings in the last 24h: {warnCount}");
                    if (warnCount == 0)
                        statsBuilder.Append(" ").Append(BotReactionsHandler.RandomPositiveReaction);
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
    }
}
