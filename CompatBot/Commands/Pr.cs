using System;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using SharpCompress.Readers;

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
                var downloadHeader = "Windows PR Build";
                var downloadText = "⏳ Pending...";
                var linuxDownloadHeader = "Linux PR Build";
                string linuxDownloadText = null;

                // windows build
                if (prInfo.StatusesUrl is string statusesUrl)
                {
                    if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                    {
                        if (artifactInfo.Artifact.Created is DateTime buildTime)
                            downloadHeader = $"{downloadHeader} ({(DateTime.UtcNow - buildTime.ToUniversalTime()).AsTimeDeltaDescription()} ago)";
                        var name = artifactInfo.Artifact.FileName;
                        name = name.Replace("rpcs3-", "").Replace("_win64", "");
                        downloadText = $"[⏬ {name}]({artifactInfo.DownloadUrl})";
                    }
                    else
                    {
                        var statuses = await githubClient.GetStatusesAsync(statusesUrl, Config.Cts.Token).ConfigureAwait(false);
                        statuses = statuses?.Where(s => s.Context == appveyorContext).ToList();
                        downloadText = statuses?.FirstOrDefault()?.Description ?? downloadText;
                    }
                }
                else if (await appveyorClient.GetPrDownloadAsync(prInfo.Number, prInfo.CreatedAt, Config.Cts.Token).ConfigureAwait(false) is ArtifactInfo artifactInfo)
                {
                    if (artifactInfo.Artifact.Created is DateTime buildTime)
                        downloadHeader = $"{downloadHeader} ({(DateTime.UtcNow - buildTime.ToUniversalTime()).AsTimeDeltaDescription()} ago)";
                    var name = artifactInfo.Artifact.FileName;
                    name = name.Replace("rpcs3-", "").Replace("_win64", "");
                    downloadText = $"[⏬ {name}]({artifactInfo.DownloadUrl})";
                }

                // linux build
                var personalAccessToken = Config.AzureDevOpsToken;
                if (!string.IsNullOrEmpty(personalAccessToken))
                    try
                    {
                        linuxDownloadText = "⏳ Pending...";
                        var azureCreds = new VssBasicCredential("bot", personalAccessToken);
                        var azureConnection = new VssConnection(new Uri("https://dev.azure.com/nekotekina"), azureCreds);
                        var azurePipelinesClient = azureConnection.GetClient<BuildHttpClient>();
                        var projectId = new Guid("3598951b-4d39-4fad-ad3b-ff2386a649de");
                        var builds = await azurePipelinesClient.GetBuildsAsync(
                            projectId,
                            repositoryId: "RPCS3/rpcs3",
                            repositoryType: "GitHub",
                            reasonFilter: BuildReason.PullRequest,
                            cancellationToken: Config.Cts.Token
                        ).ConfigureAwait(false);
                        var filterBranch = $"refs/pull/{pr}/merge";
                        builds = builds
                            .Where(b => b.SourceBranch == filterBranch && b.TriggerInfo.TryGetValue("pr.sourceSha", out var trc) && trc.Equals(prInfo.Head?.Sha, StringComparison.InvariantCultureIgnoreCase))
                            .OrderByDescending(b => b.StartTime)
                            .ToList();
                        var latestBuild = builds.FirstOrDefault();
                        if (latestBuild != null)
                        {
                            if (latestBuild.Status == BuildStatus.Completed && latestBuild.FinishTime.HasValue)
                                linuxDownloadHeader = $"{linuxDownloadHeader} ({(DateTime.UtcNow - latestBuild.FinishTime.Value.ToUniversalTime()).AsTimeDeltaDescription()} ago)";
                            var artifacts = await azurePipelinesClient.GetArtifactsAsync(projectId, latestBuild.Id, cancellationToken: Config.Cts.Token).ConfigureAwait(false);
                            var buildArtifact = artifacts.FirstOrDefault(a => a.Name.EndsWith(".GCC"));
                            var linuxBuild = buildArtifact?.Resource;
                            if (linuxBuild?.DownloadUrl is string downloadUrl)
                            {
                                var name = buildArtifact.Name ?? $"Linux build {latestBuild.Id}.zip";
                                if (linuxBuild.DownloadUrl.Contains("format=zip", StringComparison.InvariantCultureIgnoreCase))
                                    try
                                    {
                                        using var httpClient = HttpClientFactory.Create();
                                        using var stream = await httpClient.GetStreamAsync(downloadUrl).ConfigureAwait(false);
                                        using var zipStream = ReaderFactory.Open(stream);
                                        while (zipStream.MoveToNextEntry())
                                        {
                                            if (zipStream.Entry.Key.EndsWith(".AppImage", StringComparison.InvariantCultureIgnoreCase))
                                            {
                                                name = Path.GetFileName(zipStream.Entry.Key);
                                                break;
                                            }
                                        }
                                    }
                                    catch (Exception e2)
                                    {
                                        Config.Log.Error(e2, "Failed to get linux build filename");
                                    }
                                name = name.Replace("rpcs3-", "").Replace("_linux64", "");
                                linuxDownloadText = $"[⏬ {name}]({downloadUrl})";
                            }
                            //var linuxBuildSize = linuxBuild?.Properties.TryGetValue("artifactsize", out var artifactSizeStr) && int.TryParse(artifactSizeStr, out var linuxBuildSize);
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Error(e, "Failed to get Azure DevOps build info");
                        linuxDownloadText = null; // probably due to expired access token
                    }

                if (!string.IsNullOrEmpty(downloadText))
                    embed.AddField(downloadHeader, downloadText, true);
                if (!string.IsNullOrEmpty(linuxDownloadText))
                    embed.AddField(linuxDownloadHeader, linuxDownloadText, true);
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
