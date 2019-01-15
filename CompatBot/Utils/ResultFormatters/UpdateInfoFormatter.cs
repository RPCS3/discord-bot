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

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo info, DiscordEmbedBuilder builder = null)
        {
            if ((info?.LatestBuild?.Windows?.Download ?? info?.LatestBuild?.Linux?.Download) == null)
                return builder ?? new DiscordEmbedBuilder {Title = "Error", Description = "Error communicating with the update API. Try again later.", Color = Config.Colors.Maintenance};

            var justAppend = builder != null;
            var build = info.LatestBuild;
            var pr = build?.Pr ?? "0";
            string url = null;
            PrInfo prInfo = null;

            if (!justAppend)
            {
                if (int.TryParse(pr, out var prNum) && prNum > 0)
                {
                    prInfo = await githubClient.GetPrInfoAsync(prNum, Config.Cts.Token).ConfigureAwait(false);
                    url = prInfo?.HtmlUrl ?? "https://github.com/RPCS3/rpcs3/pull/" + pr;
                    pr = $"PR #{pr} by {prInfo?.User?.Login ?? "???"}";
                }
                else
                    pr = "PR #???";
            }
            builder = builder ?? new DiscordEmbedBuilder {Title = pr, Url = url, Description = prInfo?.Title, Color = Config.Colors.DownloadLinks};
            if (!string.IsNullOrEmpty(build?.Datetime))
            {
                var timestampInfo = build.Datetime;
                if (info.CurrentBuild?.Pr is string buildPr
                    && buildPr != info.LatestBuild?.Pr
                    && GetUpdateDelta(info) is TimeSpan timeDelta)
                    timestampInfo += $" ({timeDelta.AsTimeDeltaDescription()} newer)";
                else if (!justAppend && DateTime.TryParse(build.Datetime, out var buildDateTime) && DateTime.UtcNow.Ticks > buildDateTime.Ticks)
                    timestampInfo += $" ({(DateTime.UtcNow - buildDateTime.AsUtc()).AsTimeDeltaDescription()} ago)";

                if (justAppend)
                    builder.AddField($"Latest master build ({timestampInfo})", "This pull request has been merged, and is a part of `master` now");
                else
                    builder.AddField("Merge timestamp", timestampInfo);
            }
            return builder
                .AddField("Windows   ".FixSpaces(), GetLinkMessage(build?.Windows?.Download, true), true)
                .AddField("Linux   ".FixSpaces(), GetLinkMessage(build?.Linux?.Download, true), true);
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
