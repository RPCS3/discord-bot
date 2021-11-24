using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class UpdateInfoFormatter
    {
        private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo? info, DiscordClient client, bool includePrBody = false, DiscordEmbedBuilder? builder = null, Octokit.PullRequest? currentPrInfo = null)
        {
            if ((info?.LatestBuild?.Windows?.Download ?? info?.LatestBuild?.Linux?.Download) is null)
                return builder ?? new DiscordEmbedBuilder {Title = "Error", Description = "Error communicating with the update API. Try again later.", Color = Config.Colors.Maintenance};

            var justAppend = builder != null;
            var latestBuild = info!.LatestBuild;
            var latestPr = latestBuild?.Pr;
            var currentPr = info.CurrentBuild?.Pr;
            string? url = null;
            Octokit.PullRequest? latestPrInfo = null;

            string prDesc = "";
            if (!justAppend)
            {
                if (latestPr > 0)
                {
                    latestPrInfo = await GithubClient.GetPrInfoAsync(latestPr.Value, Config.Cts.Token).ConfigureAwait(false);
                    url = latestPrInfo?.HtmlUrl ?? "https://github.com/RPCS3/rpcs3/pull/" + latestPr;
                    var userName = latestPrInfo?.User?.Login ?? "???";
                    if (GetUserNameEmoji(client, userName) is DiscordEmoji emoji)
                        userName += " " + emoji;
                    prDesc = $"PR #{latestPr} by {userName}";
                }
                else
                    prDesc = "PR #???";

                if (currentPr > 0 && currentPr != latestPr)
                    currentPrInfo ??= await GithubClient.GetPrInfoAsync(currentPr.Value, Config.Cts.Token).ConfigureAwait(false);
            }
            var desc = latestPrInfo?.Title;
            if (includePrBody
                && latestPrInfo?.Body is string prInfoBody
                && !string.IsNullOrEmpty(prInfoBody))
                desc = $"**{desc?.TrimEnd()}**\n\n{prInfoBody}";
            desc = desc?.Trim();
            if (!string.IsNullOrEmpty(desc))
            {
                if (GithubLinksHandler.IssueMention.Matches(desc) is MatchCollection issueMatches && issueMatches.Any())
                {
                    var uniqueLinks = new HashSet<string>(10);
                    foreach (Match m in issueMatches)
                    {
                        if (m.Groups["issue_mention"].Value is string str
                            && !string.IsNullOrEmpty(str)
                            && uniqueLinks.Add(str))
                        {
                            var name = str;
                            var num = m.Groups["number"].Value;
                            if (string.IsNullOrEmpty(num))
                                num = m.Groups["also_number"].Value;
                            if (string.IsNullOrEmpty(num))
                            {
                                num = m.Groups["another_number"].Value;
                                name = "#" + num;
                                if (!string.IsNullOrEmpty(m.Groups["comment_id"].Value))
                                    name += " comment";
                            }
                            if (string.IsNullOrEmpty(num))
                                continue;

                            var commentLink = "";
                            if (!string.IsNullOrEmpty(m.Groups["comment_id"].Value))
                                commentLink = "#issuecomment-" + m.Groups["comment_id"].Value;
                            var newLink = $"[{name}](https://github.com/RPCS3/rpcs3/issues/{num}{commentLink})";
                            desc = desc.Replace(str, newLink);
                        }
                    }
                }
                if (GithubLinksHandler.CommitMention.Matches(desc) is MatchCollection commitMatches && commitMatches.Any())
                {
                    var uniqueLinks = new HashSet<string>(2);
                    foreach (Match m in commitMatches)
                    {
                        if (m.Groups["commit_mention"].Value is string lnk
                            && !string.IsNullOrEmpty(lnk)
                            && uniqueLinks.Add(lnk))
                        {
                            var num = m.Groups["commit_hash"].Value;
                            if (string.IsNullOrEmpty(num))
                                continue;

                            if (num.Length > 7)
                                num = num[..7];
                            desc = desc.Replace(lnk, $"[{num}]({lnk})");
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(desc)
                && GithubLinksHandler.ImageMarkup.Matches(desc) is MatchCollection imgMatches
                && imgMatches.Any())
            {
                var uniqueLinks = new HashSet<string>(10);
                foreach (Match m in imgMatches)
                {
                    if (m.Groups["img_markup"].Value is string str
                        && !string.IsNullOrEmpty(str)
                        && uniqueLinks.Add(str))
                    {
                        var caption = m.Groups["img_caption"].Value;
                        var link = m.Groups["img_link"].Value;
                        if (!string.IsNullOrEmpty(caption))
                            caption = " " + caption;
                        desc = desc.Replace(str, $"[🖼{caption}]({link})");
                    }
                }
            }
            desc = desc?.Trim(EmbedPager.MaxDescriptionLength);
            builder ??= new DiscordEmbedBuilder {Title = prDesc, Url = url, Description = desc, Color = Config.Colors.DownloadLinks};
            var currentCommit = currentPrInfo?.MergeCommitSha;
            var latestCommit = latestPrInfo?.MergeCommitSha;
            var buildTimestampKind = "Built";
            DateTime? latestBuildTimestamp = null, currentBuildTimestamp = null;
            if (Config.GetAzureDevOpsClient() is {} azureClient)
            {
                var currentAppveyorBuild = await azureClient.GetMasterBuildInfoAsync(currentCommit, currentPrInfo?.MergedAt?.DateTime, Config.Cts.Token).ConfigureAwait(false);
                var latestAppveyorBuild = await azureClient.GetMasterBuildInfoAsync(latestCommit, latestPrInfo?.MergedAt?.DateTime, Config.Cts.Token).ConfigureAwait(false);
                latestBuildTimestamp = latestAppveyorBuild?.FinishTime;
                currentBuildTimestamp = currentAppveyorBuild?.FinishTime;
                if (!latestBuildTimestamp.HasValue)
                {
                    buildTimestampKind = "Merged";
                    latestBuildTimestamp = currentPrInfo?.MergedAt?.DateTime;
                }
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
                    builder.AddField("Latest master build", "This pull request has been merged, and is a part of `master` now");
                builder.WithFooter($"{buildTimestampKind} on {timestampInfo}");
            }
            return builder
                .AddField("Windows download", GetLinkMessage(latestBuild?.Windows?.Download, true), true)
                .AddField("Linux download", GetLinkMessage(latestBuild?.Linux?.Download, true), true);
        }

        private static string GetLinkMessage(string? link, bool simpleName)
        {
            if (string.IsNullOrEmpty(link))
                return "No link available";

            var text = new Uri(link).Segments.Last();
            if (simpleName && text.StartsWith("rpcs3-"))
                text = text[6..];
            if (simpleName && text.Contains('_'))
                text = text.Split('_', 2)[0] + Path.GetExtension(text);

            return $"[⏬ {text}]({link}){"   ".FixSpaces()}";
        }

        private static DiscordEmoji? GetUserNameEmoji(DiscordClient client, string githubLogin)
            => client.GetEmoji(githubLogin switch
            {
#if DEBUG
                _ => client.Guilds.Values.FirstOrDefault()?.Emojis.Values.ToList().RandomElement(githubLogin.GetHashCode())?.GetDiscordName(),
#else
                "Nekotekina" => ":nekotekina:",
                "kd-11" => ":kd11:",
                "Megamouse" => ":megamouse:",
                "elad335" => ":elad:",
                "hcorion" => ":hcorion:",
                "AniLeo" => ":ani:",
                "Talkashie" => ":font:",
                "jarveson" => ":jarves:",
                "xddxd" => ":kekw:",
                "isJuhn" => "😺",
                "13xforever" => "💮",
                "RipleyTom" => ":galciv:",
                "Whatcookie" => "🍪",
                "clienthax" => ":gooseknife:",
                /*
                "VelocityRa" => null,
                "CookiePLMonster" => null,
                "Ruipin" => null,
                "rajkosto" => null,
                "dio-gh" => null,
                */
                _ => null,
#endif
            });

        public static TimeSpan? GetUpdateDelta(DateTime? latest, DateTime? current)
        {
            if (latest.HasValue && current.HasValue)
                return latest - current;
            return null;
        }

        public static TimeSpan? GetUpdateDelta(this UpdateInfo? updateInfo)
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
            if (delta.TotalMinutes < 1)
            {
                var seconds = (int)delta.TotalSeconds;
                return $"{seconds} second{(seconds == 1 ? "" : "s")}";
            }
            else if (delta.TotalHours < 1)
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
