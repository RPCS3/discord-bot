using Octokit;

namespace CompatBot.Utils.ResultFormatters;

internal static class PrInfoFormatter
{
    public static DiscordEmbedBuilder AsEmbed(this PullRequest prInfo)
    {
        var state = prInfo.GetState();
        var stateLabel = state.state == null ? null : $"[{state.state}] ";
        var title = $"{stateLabel}PR #{prInfo.Number} by {prInfo.User?.Login ?? "???"}";
        return new() {Title = title, Url = prInfo.HtmlUrl, Description = prInfo.Title, Color = state.color};
    }

    public static DiscordEmbedBuilder AsEmbed(this Issue issueInfo)
    {
        var state = issueInfo.GetState();
        var stateLabel = state.state == null ? null : $"[{state.state}] ";
        var title = $"{stateLabel}Issue #{issueInfo.Number} from {issueInfo.User?.Login ?? "???"}";
        return new() {Title = title, Url = issueInfo.HtmlUrl, Description = issueInfo.Title, Color = state.color};
    }

    public static (string? state, DiscordColor color) GetState(this PullRequest prInfo)
    {
        if (prInfo.State == ItemState.Open)
            return ("Open", Config.Colors.PrOpen);

        if (prInfo.State == ItemState.Closed)
        {
            if (prInfo.MergedAt.HasValue)
                return ("Merged", Config.Colors.PrMerged);

            return ("Closed", Config.Colors.PrClosed);
        }

        return (null, Config.Colors.DownloadLinks);
    }

    public static (string? state, DiscordColor color) GetState(this Issue issueInfo)
    {
        if (issueInfo.State == ItemState.Open)
            return ("Open", Config.Colors.PrOpen);

        if (issueInfo.State == ItemState.Closed)
            return ("Closed", Config.Colors.PrClosed);

        return (null, Config.Colors.DownloadLinks);
    }
}