﻿using System;
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
        private static readonly TimeSpan AvgBuildTime = TimeSpan.FromMinutes(30); // it's 20, but on merge we have pr + master builds
        private const string appveyorContext = "continuous-integration/appveyor/pr";

        [GroupCommand]
        public Task List(CommandContext ctx, [Description("Get information for specific PR number")] int pr) => LinkPrBuild(ctx.Client, ctx.Message, pr);

        [GroupCommand]
        public async Task List(CommandContext ctx, [Description("Get information for PRs with specified text in description. First word might be an author"), RemainingText] string searchStr = null)
        {
            var openPrList = await githubClient.GetOpenPrsAsync().ConfigureAwait(false);
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
            var prInfo = await githubClient.GetPrInfoAsync(pr).ConfigureAwait(false);
            if (prInfo.Number == 0)
            {
                await message.ReactWithAsync(Config.Reactions.Failure, prInfo.Message ?? "PR not found").ConfigureAwait(false);
                return;
            }

            var prState = prInfo.GetState();
            var embed = prInfo.AsEmbed();
            if (prState.state == "Open" || prState.state == "Closed")
            {
                var downloadHeader = "Latest PR Build Download";
                var downloadText = "Waiting for the first successful build";
                if (prInfo.StatusesUrl is string statusesUrl)
                {
                    if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                    {
                        if (artifactInfo.Artifact.Created is DateTime buildTime)
                            downloadHeader = $"{downloadHeader} ({(DateTime.UtcNow - buildTime.ToUniversalTime()).AsTimeDeltaDescription()} ago)";
                        downloadText = $"[⏬ {artifactInfo.Artifact.FileName}]({artifactInfo.DownloadUrl})";
                    }
                    else
                    {
                        var statuses = await githubClient.GetStatusesAsync(statusesUrl, Config.Cts.Token).ConfigureAwait(false);
                        statuses = statuses?.Where(s => s.Context == appveyorContext).ToList();
                        downloadText = statuses?.First().Description ?? downloadText;
                    }
                }
                else if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                {
                    if (artifactInfo.Artifact.Created is DateTime buildTime)
                        downloadHeader = $"{downloadHeader} ({buildTime.ToUniversalTime():u})";
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
                        embed.AddField("Latest master build", $"This pull request has been merged, and will be part of `master` very soon.\nPlease check again in {waitTime.AsTimeDeltaDescription()}.");
                    }
                }
            }
            await message.RespondAsync(embed: embed).ConfigureAwait(false);
        }

        public static async Task LinkIssue(DiscordClient client, DiscordMessage message, int issue)
        {
            var issueInfo = await githubClient.GetIssueInfoAsync(issue).ConfigureAwait(false);
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
