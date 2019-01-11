using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppveyorClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using GithubClient.POCOs;

namespace CompatBot.Commands
{
    [Group("pr"), TriggersTyping]
    [Description("Commands to list opened pull requests information")]
    internal sealed class Pr: BaseCommandModuleCustom
    {
        private static readonly GithubClient.Client githubClient = new GithubClient.Client();
        private static readonly AppveyorClient.Client appveyorClient = new AppveyorClient.Client();
        private static readonly CompatApiClient.Client compatApiClient = new CompatApiClient.Client();
        private static readonly TimeSpan AvgBuildTime = TimeSpan.FromMinutes(20);
        private const string appveyorContext = "continuous-integration/appveyor/pr";

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

            const int maxTitleLength = 80;
            var maxNum = openPrList.Max(pr => pr.Number).ToString().Length + 1;
            var maxAuthor = openPrList.Max(pr => pr.User.Login.Length);
            var maxTitle = Math.Min(openPrList.Max(pr => pr.Title.Length), maxTitleLength);
            var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n");
            foreach (var pr in openPrList)
                result.Append("`").Append($"{("#" + pr.Number).PadLeft(maxNum)} by {pr.User.Login.PadRight(maxAuthor)}: {pr.Title.Trim(maxTitleLength).PadRight(maxTitle)}".FixSpaces()).AppendLine($"` <{pr.HtmlUrl}>");
            await ctx.SendAutosplitMessageAsync(result, blockStart: null, blockEnd: null).ConfigureAwait(false);
        }

        public static async Task LinkPrBuild(DiscordClient client, DiscordMessage message, int pr)
        {
            var prInfo = await githubClient.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false);
            if (prInfo.Number == 0)
            {
                await message.ReactWithAsync(client, Config.Reactions.Failure, prInfo.Message ?? "PR not found").ConfigureAwait(false);
                return;
            }

            var prState = prInfo.GetState();
            var embed = prInfo.AsEmbed();
            if (prState.state == "Open" || prState.state == "Closed")
            {
                var downloadHeader = "Latest PR Build Download";
                var downloadText = "";
                if (prInfo.StatusesUrl is string statusesUrl)
                {
                    var statuses = await githubClient.GetStatusesAsync(statusesUrl, Config.Cts.Token).ConfigureAwait(false);
                    statuses = statuses?.Where(s => s.Context == appveyorContext).ToList();
                    if (statuses?.Count > 0)
                    {
                        if (statuses.FirstOrDefault(s => s.State == "success") is StatusInfo statusSuccess)
                        {
                            var artifactInfo = await appveyorClient.GetPrDownloadAsync(statusSuccess.TargetUrl, Config.Cts.Token).ConfigureAwait(false);
                            if (artifactInfo == null)
                                downloadText = $"[⏬ {statusSuccess.Description}]({statusSuccess.TargetUrl})";
                            else
                            {
                                if (artifactInfo.Artifact.Created is DateTime buildTime)
                                    downloadHeader = $"{downloadHeader} ({buildTime:u})";
                                downloadText = $"[⏬ {artifactInfo.Artifact.FileName}]({artifactInfo.DownloadUrl})";
                            }
                        }
                        else if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                        {
                            if (artifactInfo.Artifact.Created is DateTime buildTime)
                                downloadHeader = $"{downloadHeader} ({buildTime:u})";
                            downloadText = $"[⏬ {artifactInfo.Artifact.FileName}]({artifactInfo.DownloadUrl})";
                        }
                        else
                            downloadText = statuses.First().Description;
                    }
                }
                else if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                {
                    if (artifactInfo.Artifact.Created is DateTime buildTime)
                        downloadHeader = $"{downloadHeader} ({buildTime:u})";
                    downloadText = $"[⏬ {artifactInfo.Artifact.FileName}]({artifactInfo.DownloadUrl})";
                }
                if (!string.IsNullOrEmpty(downloadText))
                    embed.AddField(downloadHeader, downloadText);
            }
            else if (prState.state == "Merged")
            {
                var mergeTime = prInfo.MergedAt.GetValueOrDefault();
                var now = DateTime.UtcNow;
                var updateInfo = await compatApiClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
                if (updateInfo != null)
                {
                    if (DateTime.TryParse(updateInfo.LatestBuild?.Datetime, out var masterBuildTime) && masterBuildTime.Ticks >= mergeTime.Ticks)
                        embed = await updateInfo.AsEmbedAsync(embed).ConfigureAwait(false);
                    else
                    {
                        var waitTime = TimeSpan.FromMinutes(5);
                        if (now < (mergeTime + AvgBuildTime))
                            waitTime = mergeTime + AvgBuildTime - now;
                        embed.AddField("Latest master build", $"This pull request has been merged, and will be part of `master` very soon.\nPlease check again in {waitTime.GetTimeDeltaDescription()}.");
                    }
                }
            }
            await message.RespondAsync(embed: embed).ConfigureAwait(false);
        }
    }
}
