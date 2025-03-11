using CompatApiClient.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using Microsoft.TeamFoundation.Build.WebApi;
using BuildStatus = Microsoft.TeamFoundation.Build.WebApi.BuildStatus;

namespace CompatBot.Commands;

[Command("pr"), TriggersTyping]
[Description("Commands to list opened pull requests information")]
internal sealed class Pr
{
    private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);
    private static readonly CompatApiClient.Client CompatApiClient = new();

    [Command("search"), DefaultGroupCommand]
    public Task List(CommandContext ctx, [Description("Get information for specific PR number")] int pr) => LinkPrBuild(ctx.Client, ctx.Message, pr);

    [Command("search"), DefaultGroupCommand]
    public async Task List(CommandContext ctx, [Description("Get information for PRs with specified text in description. First word might be an author"), RemainingText] string? searchStr = null)
    {
        var openPrList = await GithubClient.GetOpenPrsAsync(Config.Cts.Token).ConfigureAwait(false);
        if (openPrList == null)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't retrieve open pull requests list, try again later").ConfigureAwait(false);
            return;
        }

        if (openPrList.Count == 0)
        {
            await ctx.Channel.SendMessageAsync("It looks like there are no open pull requests at the moment 🎉").ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(searchStr))
        {
            var filteredList = openPrList.Where(
                pr => pr.Title?.Contains(searchStr, StringComparison.InvariantCultureIgnoreCase) is true
                      || pr.User?.Login?.Contains(searchStr, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
            if (filteredList.Count == 0)
            {
                var searchParts = searchStr.Split(' ', 2);
                if (searchParts.Length == 2)
                {
                    var author = searchParts[0].Trim();
                    var substr = searchParts[1].Trim();
                    openPrList = openPrList.Where(
                        pr => pr.User?.Login?.Contains(author, StringComparison.InvariantCultureIgnoreCase) is true
                              && pr.Title?.Contains(substr, StringComparison.InvariantCultureIgnoreCase) is true
                    ).ToList();
                }
                else
                    openPrList = filteredList;
            }
            else
                openPrList = filteredList;
        }

        if (openPrList.Count == 0)
        {
            await ctx.Channel.SendMessageAsync("No open pull requests were found for specified filter").ConfigureAwait(false);
            return;
        }

        if (openPrList.Count == 1)
        {
            await LinkPrBuild(ctx.Client, ctx.Message, openPrList[0].Number).ConfigureAwait(false);
            return;
        }

        var responseChannel = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
        const int maxTitleLength = 80;
        var maxNum = openPrList.Max(pr => pr.Number).ToString().Length + 1;
        var maxAuthor = openPrList.Max(pr => (pr.User?.Login).GetVisibleLength());
        var maxTitle = Math.Min(openPrList.Max(pr => pr.Title.GetVisibleLength()), maxTitleLength);
        var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n");
        foreach (var pr in openPrList)
            result.Append('`').Append($"{("#" + pr.Number).PadLeft(maxNum)} by {pr.User?.Login?.PadRightVisible(maxAuthor)}: {pr.Title?.Trim(maxTitleLength).PadRightVisible(maxTitle)}".FixSpaces()).AppendLine($"` <{pr.HtmlUrl}>");
        await responseChannel.SendAutosplitMessageAsync(result, blockStart: null, blockEnd: null).ConfigureAwait(false);
    }

    [Command("link"), TextAlias("old")]
    [Description("Links to the official binary builds produced after specified PR was merged")]
    public Task Link(CommandContext ctx, [Description("PR number")] int pr) => LinkPrBuild(ctx.Client, ctx.Message, pr, true);
    
#if DEBUG
    [Command("stats"), RequiresBotModRole]
    public async Task Stats(CommandContext ctx)
    {
        var azureClient = Config.GetAzureDevOpsClient();
        var duration = await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false);
        await ctx.Channel.SendMessageAsync(
            $"Expected pipeline duration (using {duration.BuildCount} builds): \n" +
            $"95%: {duration.Percentile95} ({duration.Percentile95.TotalMinutes})\n" +
            $"90%: {duration.Percentile90} ({duration.Percentile90.TotalMinutes})\n" +
            $"85%: {duration.Percentile85} ({duration.Percentile85.TotalMinutes})\n" +
            $"80%: {duration.Percentile80} ({duration.Percentile80.TotalMinutes})\n" +
            $"Avg: {duration.Mean} ({duration.Mean.TotalMinutes})\n" +
            $"Dev: {duration.StdDev} ({duration.StdDev.TotalMinutes})"
        ).ConfigureAwait(false);
    }
#endif
        
    public static async Task LinkPrBuild(DiscordClient client, DiscordMessage message, int pr, bool linkOld = false)
    {
        var prInfo = await GithubClient.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false);
        if (prInfo is null or {Number: 0})
        {
            await message.ReactWithAsync(Config.Reactions.Failure, prInfo?.Title ?? "PR not found").ConfigureAwait(false);
            return;
        }

        var (state, _) = prInfo.GetState();
        var embed = prInfo.AsEmbed();
        var azureClient = Config.GetAzureDevOpsClient();
        if (state is "Open" or "Closed")
        {
            var windowsDownloadHeader = "Windows PR Build";
            var linuxDownloadHeader = "Linux PR Build";
            var macDownloadHeader = "Mac PR Build";
            string? windowsDownloadText = null;
            string? linuxDownloadText = null;
            string? macDownloadText = null;
            string? buildTime = null;

            if (azureClient != null && prInfo.Head?.Sha is string commit)
                try
                {
                    windowsDownloadText = "⏳ Pending…";
                    linuxDownloadText = "⏳ Pending…";
                    macDownloadText = "⏳ Pending…";
                    var latestBuild = await azureClient.GetPrBuildInfoAsync(commit, prInfo.MergedAt?.DateTime, pr, Config.Cts.Token).ConfigureAwait(false);
                    if (latestBuild == null)
                    {
                        if (state == "Open")
                        {
                            embed.WithFooter($"Opened on {prInfo.CreatedAt:u} ({(DateTime.UtcNow - prInfo.CreatedAt).AsTimeDeltaDescription()} ago)");
                        }
                        windowsDownloadText = null;
                        linuxDownloadText = null;
                        macDownloadText = null;
                    }
                    else
                    {
                        bool shouldHaveArtifacts = false;

                        if (latestBuild is
                            {
                                Status: BuildStatus.Completed,
                                Result: BuildResult.Succeeded or BuildResult.PartiallySucceeded,
                                FinishTime: not null
                            })
                        {
                            buildTime = $"Built on {latestBuild.FinishTime:u} ({(DateTime.UtcNow - latestBuild.FinishTime.Value).AsTimeDeltaDescription()} ago)";
                            shouldHaveArtifacts = true;
                        }

                        // Check for subtask errors (win/lin/mac)
                        if (latestBuild.Result is BuildResult.Failed or BuildResult.Canceled)
                        {
                            windowsDownloadText = $"❌ {latestBuild.Result}";
                        }
                        /*
                        if (latestBuild.LinuxBuild?.Status is TaskStatus.Aborted or TaskStatus.Failed or TaskStatus.Skipped)
                        {
                            linuxDownloadText = $"❌ {latestBuild.LinuxBuild?.Status}";
                        }
                        if (latestBuild.MacBuild?.Status is TaskStatus.Aborted or TaskStatus.Failed or TaskStatus.Skipped)
                        {
                            macDownloadText = $"❌ {latestBuild.MacBuild?.Status}";
                        }
                        */

                        // Check estimated time for pending builds
                        if (latestBuild is { Status: BuildStatus.InProgress, StartTime: not null })
                        {
                            var estimatedCompletionTime = latestBuild.StartTime.Value + (await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false)).Mean;
                            var estimatedTime = TimeSpan.FromMinutes(1);
                            if (estimatedCompletionTime > DateTime.UtcNow)
                                estimatedTime = estimatedCompletionTime - DateTime.UtcNow;
                            windowsDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}…";
                            linuxDownloadText = windowsDownloadText;
                            macDownloadText = windowsDownloadText;

                            /*
                            if (latestBuild.WindowsBuild?.Status is TaskStatus.Executing)
                            {
                                windowsDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}…";
                            }
                            if (latestBuild.LinuxBuild?.Status is TaskStatus.Executing)
                            {
                                linuxDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}…";
                            }
                            if (latestBuild.MacBuild?.Status is TaskStatus.Executing)
                            {
                                macDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}…";
                            }
                        */
                        }

                        // windows build
                        var name = latestBuild.WindowsFilename ?? "Windows PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_win64", "");
                        if (!string.IsNullOrEmpty(latestBuild.WindowsBuildDownloadLink))
                            windowsDownloadText = $"[⏬ {name}]({latestBuild.WindowsBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if (latestBuild.FinishTime.HasValue && (DateTime.UtcNow - latestBuild.FinishTime.Value).TotalDays > 30)
                                windowsDownloadText = "No longer available";
                        }

                        // linux build
                        name = latestBuild.LinuxFilename ?? "Linux PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_linux64", "");
                        if (!string.IsNullOrEmpty(latestBuild.LinuxBuildDownloadLink))
                            linuxDownloadText = $"[⏬ {name}]({latestBuild.LinuxBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if (latestBuild.FinishTime.HasValue && (DateTime.UtcNow - latestBuild.FinishTime.Value).TotalDays > 30)
                                linuxDownloadText = "No longer available";
                        }

                        // mac build
                        name = latestBuild.MacFilename ?? "Mac PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_macos", "");
                        if (!string.IsNullOrEmpty(latestBuild.MacBuildDownloadLink))
                            macDownloadText = $"[⏬ {name}]({latestBuild.MacBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if (latestBuild.FinishTime.HasValue && (DateTime.UtcNow - latestBuild.FinishTime.Value).TotalDays > 30)
                                macDownloadText = "No longer available";
                        }

                        // Neatify PR's with missing builders
                        /*if (latestBuild.WindowsBuild?.Status is null)
                        {
                            windowsDownloadText = null;
                        }
                        if (latestBuild.LinuxBuild?.Status is null)
                        {
                            linuxDownloadText = null;
                        }
                        if (latestBuild.MacBuild?.Status is null)
                        {
                            macDownloadText = null;
                        }*/
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Failed to get CI build info");
                    windowsDownloadText = null; // probably due to expired access token
                    linuxDownloadText = null;
                    macDownloadText = null;
                }

            if (!string.IsNullOrEmpty(windowsDownloadText))
                embed.AddField(windowsDownloadHeader, windowsDownloadText, true);
            if (!string.IsNullOrEmpty(linuxDownloadText))
                embed.AddField(linuxDownloadHeader, linuxDownloadText, true);
            if (!string.IsNullOrEmpty (macDownloadText))
                embed.AddField(macDownloadHeader, macDownloadText, true);
            if (!string.IsNullOrEmpty(buildTime))
                embed.WithFooter(buildTime);
        }
        else if (state is "Merged" && azureClient is not null)
        {
            var mergeTime = prInfo.MergedAt.GetValueOrDefault();
            var now = DateTime.UtcNow;
            var updateInfo = await CompatApiClient.GetUpdateAsync(Config.Cts.Token, linkOld ? prInfo.MergeCommitSha : null).ConfigureAwait(false);
            if (updateInfo is not null)
            {
                if (DateTime.TryParse(updateInfo.LatestBuild?.Datetime, out var masterBuildTime) && masterBuildTime.Ticks >= mergeTime.Ticks)
                    embed = await updateInfo.AsEmbedAsync(client, false, embed, prInfo, linkOld).ConfigureAwait(false);
                else
                {
                    var waitTime = TimeSpan.FromMinutes(5);
                    var avgBuildTime = (await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false)).Mean;
                    if (now < mergeTime + avgBuildTime)
                        waitTime = mergeTime + avgBuildTime - now;
                    embed.AddField("Latest master build", $"""
                        This pull request has been merged, and will be part of `master` very soon.
                        Please check again in {waitTime.AsTimeDeltaDescription()}.
                        """);
                }
            }
        }
        await message.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
    }

    public static async Task LinkIssue(DiscordClient client, DiscordMessage message, int issue)
    {
        var issueInfo = await GithubClient.GetIssueInfoAsync(issue, Config.Cts.Token).ConfigureAwait(false);
        if (issueInfo is null or {Number: 0})
            return;

        if (issueInfo.PullRequest != null)
        {
            await LinkPrBuild(client, message, issue).ConfigureAwait(false);
            return;
        }

        await message.Channel.SendMessageAsync(embed: issueInfo.AsEmbed()).ConfigureAwait(false);
    }
}