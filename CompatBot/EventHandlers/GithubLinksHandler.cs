using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal sealed class GithubLinksHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        private static readonly Regex IssueMention = new Regex(@"(issue|pr|pull[ \-]request|bug)\s*#?\s*(?<number>\d+)", DefaultOptions);
        private static readonly Regex IssueLink = new Regex(@"github.com/RPCS3/rpcs3/issues/(?<number>\d+)", DefaultOptions);
        private static readonly GithubClient.Client Client = new GithubClient.Client();

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
            foreach (var msg in lastBotMessages)
                if (BotShutupHandler.NeedToSilence(msg))
                    return;

            lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, Config.ProductCodeLookupHistoryThrottle).ConfigureAwait(false);
            StringBuilder previousRepliesBuilder = null;
            foreach (var msg in lastBotMessages)
            {
                if (msg.Author.IsCurrent)
                {
                    previousRepliesBuilder = previousRepliesBuilder ?? new StringBuilder();
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

            await args.Message.ReactWithAsync(args.Client, Config.Reactions.PleaseWait).ConfigureAwait(false);
            var suffix = issuesToLookup.Count == 1 ? "" : "s";
            try
            {
                var result = new StringBuilder($"Link{suffix} to the mentioned issue{suffix}:");
                foreach (var issueId in issuesToLookup)
                    result.AppendLine().Append("https://github.com/RPCS3/rpcs3/issues/" + issueId);
                await args.Channel.SendAutosplitMessageAsync(result, blockStart: null, blockEnd: null).ConfigureAwait(false);
            }
            finally
            {
                await args.Message.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }

        public static List<string> GetIssueIds(string input)
        {
            return IssueMention.Matches(input)
                .Select(match => match.Groups["number"].Value)
                .Distinct()
                .ToList();
        }
        public static HashSet<string> GetIssueIdsFromLinks(string input)
        {
            return new HashSet<string>(IssueLink.Matches(input).Select(match => match.Groups["number"].Value));
        }
    }
}
