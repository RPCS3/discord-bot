using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class IsTheGamePlayableHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        private static readonly Regex GameNameStatusMention = new Regex(@"((is|does|can I play)\s+|(?<dumb>^))(?<game_title>.+?)(\s+((now|currently|at all|possibly)\s+)?((is )?playable|work(s|ing)?)(?(dumb)\?))", DefaultOptions);
        private static readonly ConcurrentDictionary<ulong, DateTime> CooldownBuckets = new ConcurrentDictionary<ulong, DateTime>();
        private static readonly TimeSpan CooldownThreshold = TimeSpan.FromSeconds(5);
        private static readonly Client Client = new Client();

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Author.IsBot)
                return;

#if !DEBUG
            if (!(args.Channel.Id == Config.BotGeneralChannelId
                  || args.Channel.Name.Equals("help", StringComparison.InvariantCultureIgnoreCase)))
                return;
#endif

            if (CooldownBuckets.TryGetValue(args.Channel.Id, out var lastCheck)
                && DateTime.UtcNow - lastCheck < CooldownThreshold)
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

#if !DEBUG
            if (args.Author.IsSmartlisted(args.Client, args.Guild))
                return;
#endif

            var matches = GameNameStatusMention.Matches(args.Message.Content);
            if (!matches.Any())
                return;

            var gameTitle = matches.First().Groups["game_title"].Value.Trim();
            if (string.IsNullOrEmpty(gameTitle))
                return;

            gameTitle = gameTitle.Trim(40);
            if (gameTitle.Equals("persona 5", StringComparison.InvariantCultureIgnoreCase)
                || gameTitle.Equals("p5", StringComparison.InvariantCultureIgnoreCase))
                gameTitle = "unnamed";

            if (ProductCodeLookup.ProductCode.IsMatch(args.Message.Content))
                return;

            var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
            foreach (var msg in lastBotMessages)
                if (BotShutupHandler.NeedToSilence(msg).needToChill)
                    return;

            try
            {
                var requestBuilder = RequestBuilder.Start().SetSearch(gameTitle);
                var status = await Client.GetCompatResultAsync(requestBuilder, Config.Cts.Token).ConfigureAwait(false);
                if ((status.ReturnCode == 0 || status.ReturnCode == 2) && status.Results.Any())
                {
                    var info = status.GetSortedList().First().Value;
                    var score = CompatApiResultUtils.GetScore(gameTitle, info);
                    Config.Log.Debug($"Looked up \"{gameTitle}\", got \"{info.Title}\" with score {score}");
                    if (score < 0.2)
                        return;

                    var botSpamChannel = await args.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
                    var msg = $"{args.Author.Mention} {info.Title} is {info.Status.ToLowerInvariant()} since {info.ToUpdated()}\n" +
                              $"for more results please use compatibility list (<https://rpcs3.net/compatibility>) or `{Config.CommandPrefix}c` command in {botSpamChannel.Mention} (`!c {gameTitle.Sanitize()}`)";
                    await args.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                    CooldownBuckets[args.Channel.Id] = DateTime.UtcNow;
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }
    }
}
