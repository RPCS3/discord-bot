using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using GithubClient.POCOs;

namespace CompatBot.Commands
{
    [Group("pr")]
    [Description("Commands to list opened pull requests information")]
    internal sealed class Pr: BaseCommandModuleCustom
    {
        private static readonly GithubClient.Client githubClient = new GithubClient.Client();
        private static readonly AppveyorClient.Client appveyorClient = new AppveyorClient.Client();
        private const string appveyorContext = "continuous-integration/appveyor/pr";

        [GroupCommand]
        public async Task List(CommandContext ctx, [Description("Get information for specific PR number")] int pr)
        {
            var prInfo = await githubClient.GetPrInfoAsync(pr.ToString(), Config.Cts.Token).ConfigureAwait(false);
            if (prInfo.Number == 0)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, prInfo.Message ?? "PR not found").ConfigureAwait(false);
                return;
            }

            var embed = prInfo.AsEmbed();
            if (prInfo.State == "open")
            {
                if (prInfo.StatusesUrl is string statusesUrl)
                {
                    var statuses = await githubClient.GetStatusesAsync(statusesUrl, Config.Cts.Token).ConfigureAwait(false);
                    statuses = statuses?.Where(s => s.Context == appveyorContext).ToList();
                    var downloadHeader = "PR Build Download";
                    var downloadText = "";
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
                        else
                            downloadText = statuses.First().Description;
                    }
                    if (!string.IsNullOrEmpty(downloadText))
                        embed.AddField(downloadHeader, downloadText);
                }
            }
            await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
        }

        [GroupCommand]
        public async Task List(CommandContext ctx, [Description("Get information for PRs with specified text in description"), RemainingText] string searchStr = null)
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
                await List(ctx, openPrList[0].Number).ConfigureAwait(false);
                return;
            }

            var maxNum = openPrList.Max(pr => pr.Number).ToString().Length + 1;
            var result = new StringBuilder($"There are {openPrList.Count} open pull requests:\n```");
            foreach (var pr in openPrList)
                result.Append(("#" + pr.Number).PadLeft(maxNum)).AppendLine($" by {pr.User.Login}: {pr.Title.Trim(80)}");
            result.Append("```");
            await ctx.SendAutosplitMessageAsync(result).ConfigureAwait(false);
        }
    }
}
