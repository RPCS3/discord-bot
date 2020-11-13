using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class GithubLinksHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        public static readonly Regex IssueMention = new Regex(@"(?<issue_mention>\b(issue|pr|pull[ \-]request|bug)\s*#?\s*(?<number>\d+)|(?=\W|^)#(?<also_number>\d{4})|(https?://)github.com/RPCS3/rpcs3/(issues|pull)/(?<another_number>\d+))\b", DefaultOptions);
        public static readonly Regex CommitMention = new Regex(@"(?<commit_mention>(https?://)github.com/RPCS3/rpcs3/commit/(?<commit_hash>[0-9a-f]+))\b", DefaultOptions);
        public static readonly Regex ImageMarkup = new Regex(@"(?<img_markup>!\[(?<img_caption>[^\]]+)\]\((?<img_link>\w+://[^\)]+)\))", DefaultOptions);
        private static readonly Regex IssueLink = new Regex(@"github.com/RPCS3/rpcs3/issues/(?<number>\d+)", DefaultOptions);

        public static async Task OnMessageCreated(DiscordClient c, MessageCreateEventArgs args)
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
                    previousRepliesBuilder ??= new StringBuilder();
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
                    await Pr.LinkIssue(c, args.Message, issueId).ConfigureAwait(false);
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
            return IssueMention.Matches(input)
                .SelectMany(match => new[] {match.Groups["number"].Value, match.Groups["also_number"].Value})
                .Distinct()
                .Select(n => {
                            _ = int.TryParse(n, out var i);
                            return i;
                        })
                .Where(n => n > 0)
                .ToList();
        }
        public static HashSet<int> GetIssueIdsFromLinks(string input)
        {
            return new HashSet<int>(
                IssueLink.Matches(input)
                    .Select(match =>
                            {
                                _ = int.TryParse(match.Groups["number"].Value, out var n);
                                return n;
                            })
            );
        }
    }
}
