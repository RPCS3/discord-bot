using System.Linq;
using System.Threading.Tasks;
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
        private const string appveyorContext = "continuous-integration/appveyor/pr";

        [GroupCommand]
        public async Task List(CommandContext ctx, [Description("Get information for specific PR number")] int pr)
        {
            var prInfo = await githubClient.GetPrInfoAsync(pr.ToString(), Config.Cts.Token).ConfigureAwait(false);
            var embed = prInfo.AsEmbed();
            if (prInfo.State == "open")
            {
                if (prInfo.StatusesUrl is string statusesUrl)
                {
                    var statuses = await githubClient.GetStatusesAsync(statusesUrl, Config.Cts.Token).ConfigureAwait(false);
                    statuses = statuses?.Where(s => s.Context == appveyorContext).ToList();
                    var downloadText = "";
                    if (statuses?.Count > 0)
                    {
                        if (statuses.FirstOrDefault(s => s.State == "success") is StatusInfo statusSuccess)
                            downloadText = $"[⏬ {statusSuccess.Description}]({statusSuccess.TargetUrl})";
                        else
                            downloadText = statuses.First().Description;
                    }
                    if (!string.IsNullOrEmpty(downloadText))
                        embed.AddField("AppVeyor Download", downloadText);
                }
            }
            await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
        }
    }
}
