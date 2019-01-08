using DSharpPlus.Entities;
using GithubClient.POCOs;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class PrInfoFormatter
    {
        public static DiscordEmbedBuilder AsEmbed(this PrInfo prInfo)
        {
            (string, DiscordColor) state = prInfo.GetState();
            var stateLabel = state.Item1 == null ? null : $"[{state.Item1}] ";
            var pr = $"{stateLabel}PR #{prInfo.Number} by {prInfo.User?.Login ?? "???"}";
            return new DiscordEmbedBuilder {Title = pr, Url = prInfo.HtmlUrl, Description = prInfo.Title, Color = state.Item2};
        }

        public static (string state, DiscordColor color) GetState(this PrInfo prInfo)
        {
            if (prInfo.State == "open")
                return ("Open", Config.Colors.PrOpen);

            if (prInfo.State == "closed")
            {
                if (prInfo.MergedAt.HasValue)
                    return ("Merged", Config.Colors.PrMerged);

                return ("Closed", Config.Colors.PrClosed);
            }

            return (null, Config.Colors.DownloadLinks);
        }
    }
}
