using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using Microsoft.TeamFoundation.Build.WebApi;
using Octokit;
using BuildStatus = Microsoft.TeamFoundation.Build.WebApi.BuildStatus;

namespace CompatBot.Commands;

[Command("pr")]
[Description("Commands to list opened pull requests information")]
internal sealed class Pr
{
    private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);
    private static readonly CompatApiClient.Client CompatApiClient = new();

    [Command("search")]
    [Description("Search for open pull requests")]
    public static async ValueTask List(
        SlashCommandContext ctx,
        [Description("Pull request author username on GitHub")]
        string? author = null,
        [Description("Search for text in the pull request description")]
        string? search = null)
    {
        if (author is not { Length: > 0 } && search is not { Length: > 0 })
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} At least one argument must be provided", ephemeral: true).ConfigureAwait(false);
            return;
        }
        
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        if (await GithubClient.GetOpenPrsAsync(Config.Cts.Token).ConfigureAwait(false) is not {} openPrList)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't retrieve open pull requests list, try again later", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (openPrList.Count is 0)
        {
            await ctx.RespondAsync("It looks like there are no open pull requests at the moment", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (author is {Length: >0} && search is {Length: >0})
        {
            openPrList = openPrList.Where(
                pr => pr.User?.Login?.Contains(author, StringComparison.InvariantCultureIgnoreCase) is true
                     && pr.Title?.Contains(search, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        else if (author is { Length: > 0 })
        {
            openPrList = openPrList.Where(
                pr => pr.User?.Login?.Contains(author, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        else if (search is {Length: >0})
        {
            openPrList = openPrList.Where(
                pr => pr.Title?.Contains(search, StringComparison.InvariantCultureIgnoreCase) is true
            ).ToList();
        }
        if (openPrList.Count is 0)
        {
            await ctx.RespondAsync("No open pull requests were found", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (openPrList is [{}item])
        {
            var msg = await GetPrBuildMessageAsync(ctx.Client, item.Number).ConfigureAwait(false);
            await ctx.RespondAsync(new DiscordInteractionResponseBuilder(msg).AsEphemeral(ephemeral)).ConfigureAwait(false);
            return;
        }

        const int maxTitleLength = 80;
        var maxNum = openPrList.Max(i => i.Number).ToString().Length + 1;
        var maxAuthor = openPrList.Max(i => (i.User?.Login).GetVisibleLength());
        var maxTitle = Math.Min(openPrList.Max(i => i.Title.GetVisibleLength()), maxTitleLength);
        var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n");
        foreach (var pr in openPrList)
            result.Append('`').Append($"{("#" + pr.Number).PadLeft(maxNum)} by {pr.User?.Login?.PadRightVisible(maxAuthor)}: {pr.Title?.Trim(maxTitleLength).PadRightVisible(maxTitle)}".FixSpaces()).AppendLine($"` <{pr.HtmlUrl}>");
        var pages = AutosplitResponseHelper.AutosplitMessage(result.ToString(), blockStart: null, blockEnd: null);
        await ctx.RespondAsync(pages[0], ephemeral: ephemeral).ConfigureAwait(false);
    }

    [Command("build")]
    [Description("Link the latest available PR build")]
    public static async ValueTask Build(SlashCommandContext ctx, [Description("Pull request number")] int pr)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        var response = await GetPrBuildMessageAsync(ctx.Client, pr).ConfigureAwait(false);
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder(response).AsEphemeral(ephemeral)).ConfigureAwait(false);
    }

    [Command("merge")]
    [Description("Link to the official binary build produced after the specified PR was merged")]
    public static async ValueTask Link(SlashCommandContext ctx, [Description("Pull request number")] int pr)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        var msg = await GetPrBuildMessageAsync(ctx.Client, pr, true).ConfigureAwait(false);
        await ctx.RespondAsync(new DiscordInteractionResponseBuilder(msg).AsEphemeral(ephemeral)).ConfigureAwait(false);
    }

#if DEBUG
    [Command("stats"), RequiresBotModRole]
    public static async ValueTask Stats(SlashCommandContext ctx)
    {
        var azureClient = Config.GetAzureDevOpsClient();
        var duration = await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false);
        await ctx.RespondAsync(
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

    private static async ValueTask<DiscordMessageBuilder> GetPrBuildMessageAsync(DiscordClient client, int pr, bool linkOld = false)
    {
        var prInfo = await GithubClient.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false);
        var result = new DiscordMessageBuilder();
        if (prInfo is null or {Number: 0})
            return result.WithContent($"{Config.Reactions.Failure} {prInfo?.Title ?? "PR not found"}");

        var (state, _) = prInfo.GetState();
        var embed = prInfo.AsEmbed();
        if (state is "Open" or "Closed")
        {
            var windowsDownloadHeader = "Windows x64 PR Build";
            var linuxDownloadHeader = "Linux x64 PR Build";
            var macDownloadHeader = "Mac Intel PR Build";
            var windowsArmDownloadHeader = "Windows ARM64 PR Build";
            var linuxArmDownloadHeader = "Linux ARM64 PR Build";
            var macArmDownloadHeader = "Mac Apple Silicon PR Build";
            string? windowsDownloadText = null;
            string? linuxDownloadText = null;
            string? macDownloadText = null;
            string? windowsArmDownloadText = null;
            string? linuxArmDownloadText = null;
            string? macArmDownloadText = null;
            string? buildTime = null;

            if (prInfo is {Head.Sha: {Length: >0} commit})
                try
                {
                    windowsDownloadText = "⏳ Pending…";
                    linuxDownloadText = "⏳ Pending…";
                    macDownloadText = "⏳ Pending…";
                    windowsArmDownloadText = "⏳ Pending…";
                    linuxArmDownloadText = "⏳ Pending…";
                    macArmDownloadText = "⏳ Pending…";
                    var ghBuild = await GithubClient.GetPrBuildInfoAsync(commit, prInfo.MergedAt?.DateTime, pr, Config.Cts.Token).ConfigureAwait(false);
                    if (ghBuild is null)
                    {
                        if (state is "Open")
                        {
                            embed.WithFooter($"Opened on {prInfo.CreatedAt:u} ({(DateTime.UtcNow - prInfo.CreatedAt).AsTimeDeltaDescription()} ago)");
                        }
                        windowsDownloadText = null;
                        linuxDownloadText = null;
                        macDownloadText = null;
                        windowsArmDownloadText = null;
                        linuxArmDownloadText = null;
                        macArmDownloadText = null;
                    }
                    if (ghBuild is not null)
                    {
                        var shouldHaveArtifacts = false;
                        if (ghBuild is
                            {
                                Status: WorkflowRunStatus.Completed,
                                Result: WorkflowRunConclusion.Success
                            })
                        {
                            buildTime = $"Built on {ghBuild.FinishTime:u} ({(DateTime.UtcNow - ghBuild.FinishTime).AsTimeDeltaDescription()} ago)";
                            shouldHaveArtifacts = true;
                        }

                        // Check for subtask errors (win/lin/mac)
                        if (ghBuild is { Result: WorkflowRunConclusion.Failure or WorkflowRunConclusion.Cancelled or WorkflowRunConclusion.TimedOut })
                        {
                            windowsDownloadText = $"❌ {ghBuild.Result}";
                            linuxDownloadText = $"❌ {ghBuild.Result}";
                            windowsArmDownloadText = $"❌ {ghBuild.Result}";
                            linuxArmDownloadText = $"❌ {ghBuild.Result}";
                        }

                        // Check estimated time for pending builds
                        if (ghBuild is { Status: WorkflowRunStatus.Waiting or WorkflowRunStatus.Pending or WorkflowRunStatus.InProgress })
                        {
                            var estimatedCompletionTime = ghBuild.StartTime + (await GithubClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false)).Mean;
                            var estimatedTime = TimeSpan.FromMinutes(1);
                            if (estimatedCompletionTime > DateTime.UtcNow)
                                estimatedTime = estimatedCompletionTime - DateTime.UtcNow;
                            windowsDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}…";
                            linuxDownloadText = windowsDownloadText;
                            //macDownloadText = windowsDownloadText;
                            windowsArmDownloadText = windowsDownloadText;
                            linuxArmDownloadText = windowsDownloadText;
                            //macArmDownloadText = windowsDownloadText;
                        }

                        // windows build
                        var name = ghBuild.WindowsFilename ?? "Windows PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_win64", "").Replace("_msvc", "");
                        if (ghBuild.WindowsBuildDownloadLink is {Length: >0})
                            windowsDownloadText = $"[⏬ {name}]({ghBuild.WindowsBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                windowsDownloadText = "No longer available";
                        }

                        // windows arm build
                        name = ghBuild.WindowsArmFilename ?? "Windows ARM64 PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_win64", "").Replace("_aarch64", "").Replace("_clang", "");
                        if (ghBuild.WindowsArmBuildDownloadLink is {Length: >0})
                            windowsArmDownloadText = $"[⏬ {name}]({ghBuild.WindowsArmBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                windowsArmDownloadText = "No longer available";
                        }

                        // linux build
                        name = ghBuild.LinuxFilename ?? "Linux PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_linux64", "");
                        if (ghBuild.LinuxBuildDownloadLink is {Length: >0})
                            linuxDownloadText = $"[⏬ {name}]({ghBuild.LinuxBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                linuxDownloadText = "No longer available";
                        }

                        // linux arm build
                        name = ghBuild.LinuxArmFilename ?? "Linux ARM64 PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_linux_aarch64", "");
                        if (ghBuild.LinuxArmBuildDownloadLink is {Length: >0})
                            linuxArmDownloadText = $"[⏬ {name}]({ghBuild.LinuxArmBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                linuxArmDownloadText = "No longer available";
                        }

                        // mac build
                        name = ghBuild.MacFilename ?? "Mac PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_macos", "");
                        if (ghBuild.MacBuildDownloadLink is {Length: >0})
                            macDownloadText = $"[⏬ {name}]({ghBuild.MacBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                macDownloadText = "No longer available";
                        }

                        // mac arm build
                        name = ghBuild.MacArmFilename ?? "Mac Apple Silicon PR Build";
                        name = name.Replace("rpcs3-", "").Replace("_macos", "").Replace("_arm64", "");
                        if (ghBuild.MacArmBuildDownloadLink is {Length: >0})
                            macArmDownloadText = $"[⏬ {name}]({ghBuild.MacArmBuildDownloadLink})";
                        else if (shouldHaveArtifacts)
                        {
                            if ((DateTime.UtcNow - ghBuild.FinishTime).TotalDays > 30)
                                macArmDownloadText = "No longer available";
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Failed to get CI build info");
                    windowsDownloadText = null; // probably due to expired access token
                    linuxDownloadText = null;
                    macDownloadText = null;
                    windowsArmDownloadText = null;
                    linuxArmDownloadText = null;
                    macArmDownloadText = null;
                }

            if (!string.IsNullOrEmpty(windowsDownloadText))
                embed.AddField(windowsDownloadHeader, windowsDownloadText, true);
            if (!string.IsNullOrEmpty(linuxDownloadText))
                embed.AddField(linuxDownloadHeader, linuxDownloadText, true);
            if (!string.IsNullOrEmpty (macDownloadText))
                embed.AddField(macDownloadHeader, macDownloadText, true);
            if (!string.IsNullOrEmpty(windowsArmDownloadText))
                embed.AddField(windowsArmDownloadHeader, windowsArmDownloadText, true);
            if (!string.IsNullOrEmpty(linuxArmDownloadText))
                embed.AddField(linuxArmDownloadHeader, linuxArmDownloadText, true);
            if (!string.IsNullOrEmpty (macArmDownloadText))
                embed.AddField(macArmDownloadHeader, macArmDownloadText, true);
            if (!string.IsNullOrEmpty(buildTime))
                embed.WithFooter(buildTime);
        }
        else if (state is "Merged")
        {
            var mergeTime = prInfo.MergedAt.GetValueOrDefault();
            var now = DateTime.UtcNow;
            var updateInfo = await CompatApiClient.GetUpdateAsync(Config.Cts.Token, linkOld ? prInfo.MergeCommitSha : null).ConfigureAwait(false);
            if (updateInfo.LatestDatetime is DateTime masterBuildTime && masterBuildTime.Ticks >= mergeTime.Ticks)
                embed = await updateInfo.AsEmbedAsync(client, false, embed, prInfo, linkOld).ConfigureAwait(false);
            else
            {
                var waitTime = TimeSpan.FromMinutes(5);
                var avgBuildTime = (await GithubClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false)).Mean;
                if (now < mergeTime + avgBuildTime)
                    waitTime = mergeTime + avgBuildTime - now;
                embed.AddField(
                    "Latest master build",
                    $"""
                   This pull request has been merged, and will be part of `master` very soon.
                   Please check again in {waitTime.AsTimeDeltaDescription()}.
                   """
                );
            }
        }
        return result.AddEmbed(embed);
    }

    public static async ValueTask<DiscordMessageBuilder?> GetIssueLinkMessageAsync(DiscordClient client, int issue)
    {
        var issueInfo = await GithubClient.GetIssueInfoAsync(issue, Config.Cts.Token).ConfigureAwait(false);
        if (issueInfo is null or {Number: 0})
            return null;

        if (issueInfo.PullRequest is not null)
            return await GetPrBuildMessageAsync(client, issue).ConfigureAwait(false);

        return new DiscordMessageBuilder().AddEmbed(issueInfo.AsEmbed());
    }
}
