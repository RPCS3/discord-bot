using DSharpPlus.Entities;
using Octokit.GraphQL.Model;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class PrInfoFormatter
    {
        public static DiscordEmbedBuilder AsEmbed(this PullRequest prInfo)
        {
            var state = prInfo.GetDiscordColor();
            var stateLabel = $"[{prInfo.State}] ";
            var title = $"{stateLabel}PR #{prInfo.Number} by {prInfo.Author?.Login ?? "???"}";
            return new DiscordEmbedBuilder {Title = title, Url = prInfo.Url, Description = prInfo.Title, Color = state};
        }

        public static DiscordEmbedBuilder AsEmbed(this Issue issueInfo)
        {
            var state = issueInfo.GetDiscordColor();
            var stateLabel = $"[{state}] ";
            var title = $"{stateLabel}Issue #{issueInfo.Number} from {issueInfo.Author?.Login ?? "???"}";
            return new DiscordEmbedBuilder {Title = title, Url = issueInfo.Url, Description = issueInfo.Title, Color = state};
        }

        public static DiscordColor GetDiscordColor(this PullRequest prInfo)
        {
            if (prInfo.State == PullRequestState.Open)
                return Config.Colors.PrOpen;

            if (prInfo.State == PullRequestState.Closed)
            {
                if (prInfo.MergedAt.HasValue)
                    return Config.Colors.PrMerged;

                return Config.Colors.PrClosed;
            }

            return Config.Colors.DownloadLinks;
        }

        public static DiscordColor GetDiscordColor(this Issue issueInfo)
        {
            if (issueInfo.State == IssueState.Open)
                return Config.Colors.PrOpen;

            if (issueInfo.State == IssueState.Closed)
                return Config.Colors.PrClosed;

            return Config.Colors.DownloadLinks;
        }
    }
}
