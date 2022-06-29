using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters;

internal static class PrInfoFormatter
{
    public static DiscordEmbedBuilder AsEmbed(this Octokit.PullRequest prInfo)
    {
        var state = prInfo.GetState();
        var stateLabel = state.state == null ? null : $"[{state.state}] ";
        var title = $"{stateLabel}PR #{prInfo.Number} by {prInfo.User?.Login ?? "???"}";
        return new DiscordEmbedBuilder {Title = title, Url = prInfo.HtmlUrl, Description = prInfo.Title, Color = state.color};
    }

    public static DiscordEmbedBuilder AsEmbed(this Octokit.Issue issueInfo)
    {
        var state = issueInfo.GetState();
        var stateLabel = state.state == null ? null : $"[{state.state}] ";
        var title = $"{stateLabel}Issue #{issueInfo.Number} from {issueInfo.User?.Login ?? "???"}";
        return new DiscordEmbedBuilder {Title = title, Url = issueInfo.HtmlUrl, Description = issueInfo.Title, Color = state.color};
    }

    public static (string? state, DiscordColor color) GetState(this Octokit.PullRequest prInfo)
    {
        if (prInfo.State == Octokit.ItemState.Open)
            return ("Open", Config.Colors.PrOpen);

        if (prInfo.State == Octokit.ItemState.Closed)
        {
            if (prInfo.MergedAt.HasValue)
                return ("Merged", Config.Colors.PrMerged);

            return ("Closed", Config.Colors.PrClosed);
        }

        return (null, Config.Colors.DownloadLinks);
    }

    public static (string? state, DiscordColor color) GetState(this Octokit.Issue issueInfo)
    {
        if (issueInfo.State == Octokit.ItemState.Open)
            return ("Open", Config.Colors.PrOpen);

        if (issueInfo.State == Octokit.ItemState.Closed)
            return ("Closed", Config.Colors.PrClosed);

        return (null, Config.Colors.DownloadLinks);
    }
}