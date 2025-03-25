using System.Text.RegularExpressions;
using CompatBot.Commands;

namespace CompatBot.EventHandlers;

internal static partial class GithubLinksHandler
{
    private const RegexOptions DefaultOptions = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
    [GeneratedRegex(@"(?<issue_mention>\b(issue|pr|pull[ \-]request|bug)\s*#?\s*(?<number>\d+)|\B#(?<also_number>1?\d{4})|(https?://)github.com/RPCS3/rpcs3/(issues|pull)/(?<another_number>\d+)(#issuecomment-(?<comment_id>\d+))?)\b", DefaultOptions)]
    internal static partial Regex IssueMention();
    [GeneratedRegex(@"(?<commit_mention>(https?://)github.com/RPCS3/rpcs3/commit/(?<commit_hash>[0-9a-f]+))\b", DefaultOptions)]
    internal static partial Regex CommitMention();
    [GeneratedRegex(@"(?<img_markup>!\[(?<img_caption>[^\]]+)\]\((?<img_link>\w+://[^\)]+)\))", DefaultOptions)]
    internal static partial Regex ImageMarkup();
    [GeneratedRegex(@"github.com/RPCS3/rpcs3/issues/(?<number>\d+)", DefaultOptions)]
    internal static partial Regex IssueLink();

    public static async Task OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args)
    {
        if (DefaultHandlerFilter.IsFluff(args.Message))
            return;

        if ("media".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase))
            return;

        var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
        foreach (var msg in lastBotMessages)
            if (BotReactionsHandler.NeedToSilence(msg).needToChill)
                return;

        lastBotMessages = await args.Channel.GetMessagesBeforeCachedAsync(args.Message.Id, Config.ProductCodeLookupHistoryThrottle).ConfigureAwait(false);
        StringBuilder? previousRepliesBuilder = null;
        foreach (var msg in lastBotMessages)
        {
            if (msg.Author.IsCurrent)
            {
                previousRepliesBuilder ??= new();
                previousRepliesBuilder.AppendLine(msg.Content);
                var embeds = msg.Embeds;
                if (embeds?.Count > 0)
                    foreach (var embed in embeds)
                        previousRepliesBuilder.AppendLine(embed.Title).AppendLine(embed.Description);
            }
        }
        var previousReplies = previousRepliesBuilder?.ToString() ?? "";
        var idsFromPreviousReplies = GetIssueIdsFromLinks(previousReplies);
        var issuesToLookup = GetIssueIds(args.Message.Content)
            .Where(lnk => !idsFromPreviousReplies.Contains(lnk))
            .Take(args.Channel.IsPrivate ? 50 : 5)
            .ToList();
        if (issuesToLookup.Count == 0)
            return;

        var suffix = issuesToLookup.Count == 1 ? "" : "s";
        if (GithubClient.Client.RateLimitRemaining - issuesToLookup.Count >= 10)
        {
            foreach (var issueId in issuesToLookup)
            {
                if (await Pr.GetIssueLinkMessageAsync(c, issueId).ConfigureAwait(false) is {} msg)
                    await args.Message.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            }
        }
        else
        {
            var result = new StringBuilder($"Link{suffix} to the mentioned issue{suffix}:");
            foreach (var issueId in issuesToLookup)
                result.AppendLine().Append("https://github.com/RPCS3/rpcs3/issues/" + issueId);
            await args.Channel.SendAutosplitMessageAsync(result, blockStart: null, blockEnd: null).ConfigureAwait(false);
        }
    }

    public static List<int> GetIssueIds(string input)
    {
        return IssueMention().Matches(input)
            .SelectMany(match => new[]
            {
                match.Groups["number"].Value,
                match.Groups["also_number"].Value,
                match.Groups["another_number"].Value,
            })
            .Distinct()
            .Select(n => int.TryParse(n, out var i) ? i : default)
            .Where(n => n > 0)
            .ToList();
    }
    public static HashSet<int> GetIssueIdsFromLinks(string input)
    {
        return
        [

            ..IssueLink().Matches(input)
                .Select(match =>
                {
                    _ = int.TryParse(match.Groups["number"].Value, out var n);
                    return n;
                })

        ];
    }
}