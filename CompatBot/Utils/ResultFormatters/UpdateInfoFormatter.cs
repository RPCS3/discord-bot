using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.POCOs;
using DSharpPlus.Entities;
using GithubClient.POCOs;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class UpdateInfoFormatter
    {
        private static readonly GithubClient.Client githubClient = new GithubClient.Client();
        private static readonly AppveyorClient.Client appveyorClient = new AppveyorClient.Client();

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo info, DiscordEmbedBuilder builder = null)
        {
            if ((info?.LatestBuild?.Windows?.Download ?? info?.LatestBuild?.Linux?.Download) == null)
                return builder ?? new DiscordEmbedBuilder {Title = "Error", Description = "Error communicating with the update API. Try again later.", Color = Config.Colors.Maintenance};

            var justAppend = builder != null;
            var latestBuild = info.LatestBuild;
            var latestPr = latestBuild?.Pr;
            var currentPr = info.CurrentBuild?.Pr;
            string url = null;
            PrInfo latestPrInfo = null;
            PrInfo currentPrInfo = null;

            string prDesc = "";
            if (!justAppend)
            {
                if (latestPr > 0)
                {
                    latestPrInfo = await githubClient.GetPrInfoAsync(latestPr.Value, Config.Cts.Token).ConfigureAwait(false);
                    url = latestPrInfo?.HtmlUrl ?? "https://github.com/RPCS3/rpcs3/pull/" + latestPr;
                    prDesc = $"PR #{latestPr} by {latestPrInfo?.User?.Login ?? "???"}";
                }
                else
                    prDesc = "PR #???";

                if (currentPr > 0 && currentPr != latestPr)
                    currentPrInfo = await githubClient.GetPrInfoAsync(currentPr.Value, Config.Cts.Token).ConfigureAwait(false);
            }
            builder = builder ?? new DiscordEmbedBuilder {Title = prDesc, Url = url, Description = latestPrInfo?.Title, Color = Config.Colors.DownloadLinks};
            var currentCommit = currentPrInfo?.MergeCommitSha;
            var latestCommit = latestPrInfo?.MergeCommitSha;
            var currentAppveyorBuild = await appveyorClient.GetMasterBuildAsync(currentCommit, currentPrInfo?.MergedAt, Config.Cts.Token).ConfigureAwait(false);
            var latestAppveyorBuild = await appveyorClient.GetMasterBuildAsync(latestCommit, latestPrInfo?.MergedAt, Config.Cts.Token).ConfigureAwait(false);
            var buildTimestampKind = "Build";
            var latestBuildTimestamp = latestAppveyorBuild?.Finished?.ToUniversalTime();
            var currentBuildTimestamp = currentAppveyorBuild?.Finished?.ToUniversalTime();
            if (!latestBuildTimestamp.HasValue)
            {
                buildTimestampKind = "Merge";
                latestBuildTimestamp = latestPrInfo?.MergedAt;
                currentBuildTimestamp = currentPrInfo?.MergedAt;
            }

            if (!string.IsNullOrEmpty(latestBuild?.Datetime))
            {
                var timestampInfo = latestBuildTimestamp?.ToString("u") ?? latestBuild.Datetime;
                if (currentPr > 0
                    && currentPr != latestPr
                    && GetUpdateDelta(latestBuildTimestamp, currentBuildTimestamp) is TimeSpan timeDelta)
                    timestampInfo += $" ({timeDelta.AsTimeDeltaDescription()} newer)";
                else if (!justAppend
                         && latestBuildTimestamp.HasValue
                         && DateTime.UtcNow.Ticks > latestBuildTimestamp.Value.Ticks)
                    timestampInfo += $" ({(DateTime.UtcNow - latestBuildTimestamp.Value).AsTimeDeltaDescription()} ago)";

                if (justAppend)
                    builder.AddField($"Latest master build ({timestampInfo})", "This pull request has been merged, and is a part of `master` now");
                else
                    builder.AddField($"{buildTimestampKind} timestamp", timestampInfo);
            }
            return builder
                .AddField("Windows   ".FixSpaces(), GetLinkMessage(latestBuild?.Windows?.Download, true), true)
                .AddField("Linux   ".FixSpaces(), GetLinkMessage(latestBuild?.Linux?.Download, true), true);
        }

        private static string GetLinkMessage(string link, bool simpleName)
        {
            if (string.IsNullOrEmpty(link))
                return "No link available";

            var text = new Uri(link).Segments?.Last() ?? "";
            if (simpleName && text.StartsWith("rpcs3-"))
                text = text.Substring(6);
            if (simpleName && text.Contains('_'))
                text = text.Split('_', 2)[0] + Path.GetExtension(text);

            return $"[⏬ {text}]({link}){"   ".FixSpaces()}";
        }

        public static TimeSpan? GetUpdateDelta(DateTime? latest, DateTime? current)
        {
            if (latest.HasValue && current.HasValue)
                return latest - current;
            return null;
        }

        public static TimeSpan? GetUpdateDelta(this UpdateInfo updateInfo)
        {
            if (updateInfo?.LatestBuild?.Datetime is string latestDateTimeStr
                && DateTime.TryParse(latestDateTimeStr, out var latestDateTime)
                && updateInfo.CurrentBuild?.Datetime is string dateTimeBuildStr
                && DateTime.TryParse(dateTimeBuildStr, out var dateTimeBuild))
                return latestDateTime - dateTimeBuild;
            return null;
        }

        public static string AsTimeDeltaDescription(this TimeSpan delta)
        {
            if (delta.TotalHours < 1)
            {
                var minutes = (int)delta.TotalMinutes;
                return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
            }
            else if (delta.TotalDays < 1)
            {
                var hours = (int) delta.TotalHours;
                return $"{hours} hour{(hours == 1 ? "": "s")}";
            }
            else if (delta.TotalDays < 7)
            {
                var days = (int) delta.TotalDays;
                return $"{days} day{(days == 1 ? "": "s")}";
            }
            else if (delta.TotalDays < 30)
            {
                var weeks = (int)(delta.TotalDays/7);
                return $"{weeks} week{(weeks == 1 ? "" : "s")}";
            }
            else if (delta.TotalDays < 365)
            {
                var months = (int)(delta.TotalDays/30);
                return $"{months} month{(months == 1 ? "" : "s")}";
            }
            else
            {
                var years = (int)(delta.TotalDays/365);
                return $"{years} year{(years == 1 ? "" : "s")}";
            }
        }
    }
}
