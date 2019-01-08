using System;
using System.Linq;
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
    }
}
