using System.IO;
using System.Text.RegularExpressions;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;

namespace CompatBot.Utils.ResultFormatters;

internal static class UpdateInfoFormatter
{
    private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);

    public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo? info, DiscordClient client, bool includePrBody = false, DiscordEmbedBuilder? builder = null, Octokit.PullRequest? currentPrInfo = null, bool useCurrent = false)
    {
        if ( info is not {ReturnCode: >=StatusCode.UnknownBuild})
            return builder ?? new DiscordEmbedBuilder
            {
                Title = "Error",
                Description = "Error communicating with the update API. Try again later.",
                Color = Config.Colors.Maintenance,
            };

        var justAppend = builder is not null;
        var latestBuild = info.X64?.LatestBuild ?? info.Arm?.LatestBuild;
        var currentBuild = info.X64?.CurrentBuild ?? info.Arm?.CurrentBuild;
        var latestPr = latestBuild?.Pr;
        var currentPr = currentBuild?.Pr;
        string? url = null;
        Octokit.PullRequest? latestPrInfo = null;

        var prDesc = "";
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
        if (includePrBody && latestPrInfo?.Body is { Length: >0 } prInfoBody)
            desc = $"**{desc?.TrimEnd()}**\n\n{prInfoBody}";
        desc = desc?.Trim();
        if (desc is {Length: >0})
        {
            if (GithubLinksHandler.IssueMention().Matches(desc) is { Count: >0 } issueMatches)
            {
                var uniqueLinks = new HashSet<string>(10);
                foreach (Match m in issueMatches)
                {
                    if (m.Groups["issue_mention"].Value is { Length: >0 } str && uniqueLinks.Add(str))
                    {
                        var name = str;
                        var num = m.Groups["number"].Value;
                        if (string.IsNullOrEmpty(num))
                            num = m.Groups["also_number"].Value;
                        if (string.IsNullOrEmpty(num))
                        {
                            num = m.Groups["another_number"].Value;
                            name = "#" + num;
                            if (m.Groups["comment_id"].Value is {Length: >0})
                                name += " comment";
                        }
                        if (num is not {Length: >0})
                            continue;

                        var commentLink = "";
                        if (m.Groups["comment_id"].Value is {Length: >0})
                            commentLink = "#issuecomment-" + m.Groups["comment_id"].Value;
                        var newLink = $"[{name}](https://github.com/RPCS3/rpcs3/issues/{num}{commentLink})";
                        desc = desc.Replace(str, newLink);
                    }
                }
            }
            if (GithubLinksHandler.CommitMention().Matches(desc) is { Count: >0 } commitMatches)
            {
                var uniqueLinks = new HashSet<string>(2);
                foreach (Match m in commitMatches)
                {
                    if (m.Groups["commit_mention"].Value is { Length: >0 } lnk && uniqueLinks.Add(lnk))
                    {
                        var num = m.Groups["commit_hash"].Value;
                        if (num is not {Length: >0})
                            continue;

                        if (num.Length > 7)
                            num = num[..7];
                        desc = desc.Replace(lnk, $"[{num}]({lnk})");
                    }
                }
            }
        }
        if (desc is {Length: >0} && GithubLinksHandler.ImageMarkup().Matches(desc) is {Count: >0} imgMatches)
        {
            var uniqueLinks = new HashSet<string>(10);
            foreach (Match m in imgMatches)
            {
                if (m.Groups["img_markup"].Value is { Length: >0 } str && uniqueLinks.Add(str))
                {
                    var caption = m.Groups["img_caption"].Value;
                    var link = m.Groups["img_link"].Value;
                    if (caption is {Length: >0})
                        caption = " " + caption;
                    desc = desc.Replace(str, $"[🖼️{caption}]({link})");
                }
            }
        }
        desc = desc?.Trim(EmbedPager.MaxDescriptionLength);
        builder ??= new() {Title = prDesc, Url = url, Description = desc, Color = Config.Colors.DownloadLinks};
        var currentCommit = currentPrInfo?.MergeCommitSha;
        var latestCommit = latestPrInfo?.MergeCommitSha;
        var buildTimestampKind = "Built";
        DateTimeOffset? latestBuildTimestamp = null, currentBuildTimestamp = null;
        //if (Config.GetAzureDevOpsClient() is {} azureClient)
        {
            var currentAppveyorBuild = await GithubClient.GetMasterBuildInfoAsync(currentCommit, currentPrInfo?.MergedAt?.DateTime, Config.Cts.Token).ConfigureAwait(false);
            var latestAppveyorBuild = await GithubClient.GetMasterBuildInfoAsync(latestCommit, latestPrInfo?.MergedAt?.DateTime, Config.Cts.Token).ConfigureAwait(false);
            latestBuildTimestamp = latestAppveyorBuild?.FinishTime;
            currentBuildTimestamp = currentAppveyorBuild?.FinishTime;
            if (!latestBuildTimestamp.HasValue)
            {
                buildTimestampKind = "Merged";
                latestBuildTimestamp = currentPrInfo?.MergedAt?.DateTime;
            }
        }

        var linkedX64Build = useCurrent ? info.X64?.CurrentBuild : info.X64?.LatestBuild;
        var linkedArmBuild = useCurrent ? info.Arm?.CurrentBuild : info.Arm?.LatestBuild;
        if ((linkedX64Build ?? linkedArmBuild)?.Datetime is {Length: >0} dateTime)
        {
            var timestampInfo = (useCurrent ? currentBuildTimestamp : latestBuildTimestamp)?.ToString("u") ?? dateTime;
            if (!useCurrent
                && currentPr > 0
                && currentPr != latestPr
                && GetUpdateDelta(latestBuildTimestamp, currentBuildTimestamp) is TimeSpan timeDelta)
                timestampInfo += $" ({timeDelta.AsTimeDeltaDescription()} newer)";
            else if (!justAppend
                     && latestBuildTimestamp.HasValue
                     && DateTime.UtcNow.Ticks > latestBuildTimestamp.Value.Ticks)
                timestampInfo += $" ({(DateTime.UtcNow - latestBuildTimestamp.Value).AsTimeDeltaDescription()} ago)";

            if (justAppend)
            {
                if (useCurrent)
                    builder.AddField("Archived official build", "This is an **old** archived build for the specified pull request");
                else
                    builder.AddField("Latest official release", "This pull request has been merged, and is a part of the latest official build");
            }
            builder.WithFooter($"{buildTimestampKind} on {timestampInfo}");
        }
        return builder
            .AddField("Windows x64", GetLinkMessage(linkedX64Build?.Windows, true), true)
            .AddField("Linux x64", GetLinkMessage(linkedX64Build?.Linux, true), true)
            .AddField("Mac Intel", GetLinkMessage(linkedX64Build?.Mac, true), true)
            .AddField("Windows ARM64", GetLinkMessage(linkedArmBuild?.Windows, true), true)
            .AddField("Linux ARM64", GetLinkMessage(linkedArmBuild?.Linux, true), true)
            .AddField("Mac Apple Silicon", GetLinkMessage(linkedArmBuild?.Mac, true), true);
    }

    private static string GetLinkMessage(BuildLink? link, bool simpleName)
    {
        if (link is not {Download.Length: >0, Size: >0})
            return "No link available";

        var text = new Uri(link.Download).Segments.Last();
        if (simpleName && text.StartsWith("rpcs3-"))
            text = text[6..];
        if (simpleName && text.Contains('_'))
            text = text.Split('_', 2)[0] + Path.GetExtension(text);

        return $"[⏬ {text}]({link.Download}){"   ".FixSpaces()}";
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

    public static TimeSpan? GetUpdateDelta(DateTimeOffset? latest, DateTimeOffset? current)
    {
        if (latest.HasValue && current.HasValue)
            return latest - current;
        return null;
    }

    public static TimeSpan? GetUpdateDelta(this UpdateInfo updateInfo)
    {
        if (updateInfo is { LatestDatetime: DateTime latestDateTime, CurrentDatetime: DateTime dateTimeBuild })
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
        if (delta.TotalHours < 1)
        {
            var minutes = (int)delta.TotalMinutes;
            return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
        }
        if (delta.TotalDays < 1)
        {
            var hours = (int) delta.TotalHours;
            return $"{hours} hour{(hours == 1 ? "": "s")}";
        }
        if (delta.TotalDays < 7)
        {
            var days = (int) delta.TotalDays;
            return $"{days} day{(days == 1 ? "": "s")}";
        }
        if (delta.TotalDays < 30)
        {
            var weeks = (int)(delta.TotalDays/7);
            return $"{weeks} week{(weeks == 1 ? "" : "s")}";
        }
        if (delta.TotalDays < 365)
        {
            var months = (int)(delta.TotalDays/30);
            return $"{months} month{(months == 1 ? "" : "s")}";
        }
        var years = (int)(delta.TotalDays/365);
        return $"{years} year{(years == 1 ? "" : "s")}";
    }
}