using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.TeamFoundation.Build.WebApi;

namespace CompatBot.Commands
{
    [Group("pr"), TriggersTyping]
    [Description("Commands to list opened pull requests information")]
    internal sealed class Pr: BaseCommandModuleCustom
    {
        private static readonly GithubClient.Client githubClient = new GithubClient.Client();
        private static readonly CompatApiClient.Client compatApiClient = new CompatApiClient.Client();
        private static readonly TimeSpan AvgBuildTime = TimeSpan.FromMinutes(30);

        [GroupCommand]
        public Task List(CommandContext ctx, [Description("Get information for specific PR number")] int pr) => LinkPrBuild(ctx.Client, ctx.Message, pr);

        [GroupCommand]
        public async Task List(CommandContext ctx, [Description("Get information for PRs with specified text in description. First word might be an author"), RemainingText] string searchStr = null)
        {
            var openPrList = await githubClient.GetOpenPrsAsync(Config.Cts.Token).ConfigureAwait(false);
            if (openPrList == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't retrieve open pull requests list, try again later").ConfigureAwait(false);
                return;
            }

            if (openPrList.Count == 0)
            {
                await ctx.RespondAsync("It looks like there are no open pull requests at the moment 🎉").ConfigureAwait(false);
                return;
            }

            if (!string.IsNullOrEmpty(searchStr))
            {
                var filteredList = openPrList.Where(pr => pr.Title.Contains(searchStr, StringComparison.InvariantCultureIgnoreCase) || pr.User.Login.Contains(searchStr, StringComparison.InvariantCultureIgnoreCase)).ToList();
                if (filteredList.Count == 0)
                {
                    var searchParts = searchStr.Split(' ', 2);
                    if (searchParts.Length == 2)
                    {
                        var author = searchParts[0].Trim();
                        var substr = searchParts[1].Trim();
                        openPrList = openPrList.Where(pr => pr.User.Login.Contains(author, StringComparison.InvariantCultureIgnoreCase) && pr.Title.Contains(substr, StringComparison.InvariantCultureIgnoreCase)).ToList();
                    }
                    else
                        openPrList = filteredList;
                }
                else
                    openPrList = filteredList;
            }

            if (openPrList.Count == 0)
            {
                await ctx.RespondAsync("No open pull requests were found for specified filter").ConfigureAwait(false);
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
            var maxAuthor = openPrList.Max(pr => pr.User.Login.GetVisibleLength());
            var maxTitle = Math.Min(openPrList.Max(pr => pr.Title.GetVisibleLength()), maxTitleLength);
            var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n");
            foreach (var pr in openPrList)
                result.Append("`").Append($"{("#" + pr.Number).PadLeft(maxNum)} by {pr.User.Login.PadRightVisible(maxAuthor)}: {pr.Title.Trim(maxTitleLength).PadRightVisible(maxTitle)}".FixSpaces()).AppendLine($"` <{pr.HtmlUrl}>");
            await responseChannel.SendAutosplitMessageAsync(result, blockStart: null, blockEnd: null).ConfigureAwait(false);
        }

        public static async Task LinkPrBuild(DiscordClient client, DiscordMessage message, int pr)
        {
            var prInfo = await githubClient.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false);
            if (prInfo.Number == 0)
            {
                await message.ReactWithAsync(Config.Reactions.Failure, prInfo.Message ?? "PR not found").ConfigureAwait(false);
                return;
            }

            var prState = prInfo.GetState();
            var embed = prInfo.AsEmbed();
            if (prState.state == "Open" || prState.state == "Closed")
            {
                var windowsDownloadHeader = "Windows PR Build";
                var linuxDownloadHeader = "Linux PR Build";
                string windowsDownloadText = null;
                string linuxDownloadText = null;
                string buildTime = null;

                var azureClient = Config.GetAzureDevOpsClient();
                if (azureClient != null && prInfo.Head?.Sha is string commit)
                    try
                    {
                        windowsDownloadText = "⏳ Pending...";
                        linuxDownloadText = "⏳ Pending...";
                        var latestBuild = await azureClient.GetPrBuildInfoAsync(commit, prInfo.MergedAt, pr, Config.Cts.Token).ConfigureAwait(false);
                        if (latestBuild != null)
                        {
                            if (latestBuild.Status == BuildStatus.Completed
                                && (latestBuild.Result == BuildResult.Succeeded || latestBuild.Result == BuildResult.PartiallySucceeded)
                                && latestBuild.FinishTime.HasValue)
                                buildTime = $"Built on {latestBuild.FinishTime:u} ({(DateTime.UtcNow - latestBuild.FinishTime.Value).AsTimeDeltaDescription()} ago)";
                            else if (latestBuild.Result == BuildResult.Failed || latestBuild.Result == BuildResult.Canceled)
                                windowsDownloadText = $"❌ {latestBuild.Result}";
                            else if (latestBuild.Status == BuildStatus.InProgress && latestBuild.StartTime != null)
                            {
                                var estimatedCompletionTime = latestBuild.StartTime.Value + AvgBuildTime;
                                var estimatedTime = TimeSpan.FromMinutes(1);
                                if (estimatedCompletionTime > DateTime.UtcNow)
                                    estimatedTime = estimatedCompletionTime - DateTime.UtcNow;
                                windowsDownloadText = $"⏳ Pending in {estimatedTime.AsTimeDeltaDescription()}...";
                                linuxDownloadText = windowsDownloadText;
                            }
                            // windows build
                            var name = latestBuild.WindowsFilename ?? "Windows PR Build";
                            name = name.Replace("rpcs3-", "").Replace("_win64", "");
                            if (!string.IsNullOrEmpty(latestBuild.WindowsBuildDownloadLink))
                                windowsDownloadText = $"[⏬ {name}]({latestBuild.WindowsBuildDownloadLink})";

                            // linux build
                            name = latestBuild.LinuxFilename ?? "Linux PR Build";
                            name = name.Replace("rpcs3-", "").Replace("_linux64", "");
                            if (!string.IsNullOrEmpty(latestBuild.LinuxBuildDownloadLink))
                                linuxDownloadText = $"[⏬ {name}]({latestBuild.LinuxBuildDownloadLink})";
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Error(e, "Failed to get Azure DevOps build info");
                        linuxDownloadText = null; // probably due to expired access token
                    }

                if (!string.IsNullOrEmpty(windowsDownloadText))
                    embed.AddField(windowsDownloadHeader, windowsDownloadText, true);
                if (!string.IsNullOrEmpty(linuxDownloadText))
                    embed.AddField(linuxDownloadHeader, linuxDownloadText, true);
                if (!string.IsNullOrEmpty(buildTime))
                    embed.WithFooter(buildTime);
            }
            else if (prState.state == "Merged")
            {
                var mergeTime = prInfo.MergedAt.GetValueOrDefault();
                var now = DateTime.UtcNow;
                var updateInfo = await compatApiClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
                if (updateInfo != null)
                {
                    if (DateTime.TryParse(updateInfo.LatestBuild?.Datetime, out var masterBuildTime) && masterBuildTime.Ticks >= mergeTime.Ticks)
                        embed = await updateInfo.AsEmbedAsync(client, false, embed).ConfigureAwait(false);
                    else
                    {
                        var waitTime = TimeSpan.FromMinutes(5);
                        if (now < (mergeTime + AvgBuildTime))
                            waitTime = mergeTime + AvgBuildTime - now;
                        embed.AddField("Latest master build", $"This pull request has been merged, and will be part of `master` very soon.\nPlease check again in {waitTime.AsTimeDeltaDescription()}.");
                    }
                }
            }
            await message.RespondAsync(embed: embed).ConfigureAwait(false);
        }

        public static async Task LinkIssue(DiscordClient client, DiscordMessage message, int issue)
        {
            var issueInfo = await githubClient.GetIssueInfoAsync(issue, Config.Cts.Token).ConfigureAwait(false);
            if (issueInfo.Number == 0)
                return;

            if (issueInfo.PullRequest != null)
            {
                await LinkPrBuild(client, message, issue).ConfigureAwait(false);
                return;
            }

            await message.RespondAsync(embed: issueInfo.AsEmbed()).ConfigureAwait(false);
        }
    }
}
