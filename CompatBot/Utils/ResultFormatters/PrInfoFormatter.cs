using System.Reflection.Metadata;
using DSharpPlus.Entities;
using GithubClient.POCOs;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class PrInfoFormatter
    {
        public static DiscordEmbedBuilder AsEmbed(this PrInfo prInfo)
        {
            (string, DiscordColor) state;
            if (prInfo.State == "open")
                state = ("Open", Config.Colors.PrOpen);
            else if (prInfo.State == "closed")
                state = prInfo.MergedAt.HasValue ? ("Merged", Config.Colors.PrMerged) : ("Closed", Config.Colors.PrClosed);
            else
                state = (null, Config.Colors.DownloadLinks);

            var stateLabel = state.Item1 == null ? null : $"[{state.Item1}] ";
            var pr = $"{stateLabel}PR #{prInfo.Number} by {prInfo.User?.Login ?? "???"}";
            return new DiscordEmbedBuilder {Title = pr, Url = prInfo.HtmlUrl, Description = prInfo.Title, Color = state.Item2};
        }
    }
}
