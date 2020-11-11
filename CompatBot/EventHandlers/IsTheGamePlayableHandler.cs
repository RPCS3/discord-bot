using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers
{
    internal static class IsTheGamePlayableHandler
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture;
        private static readonly Regex GameNameStatusMention1 = new Regex(
            @"(\b((is|does|can I play|any(one|1) tr(y|ied)|how's|(wonder(ing)?|me|knows?) if)\s+)(?<game_title_1>.+?)\s+((now|currently|at all|possibly|fully|(on (this|the) )emu(lator))\s+)?((it?s )?playable|work(s|ing)?|runs?|doing))\b" +
            @"|(\b(((can I|possible to) (play|run)|any(one|1) tr(y|ied)|compat[ai]bility (with|of))\s+)(?<game_title_2>.+?)(\s+((now|currently|at all|possibly|fully)\s+)?((it?s )?playable|work(s|ing)?|on (it|this))\b|\?|$))" +
            @"|(^(?<game_title_3>.+?)\s+((is )?(playable|work(s|ing)?))\?)",
            DefaultOptions
        );
        private static readonly ConcurrentDictionary<ulong, DateTime> CooldownBuckets = new ConcurrentDictionary<ulong, DateTime>();
        private static readonly TimeSpan CooldownThreshold = TimeSpan.FromSeconds(5);
        private static readonly Client Client = new Client();

        public static async Task OnMessageCreated(DiscordClient c, MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

#if !DEBUG
            if (!(args.Channel.Id == Config.BotGeneralChannelId
                  || args.Channel.Name.Equals("help", StringComparison.InvariantCultureIgnoreCase)))
                return;

            if (CooldownBuckets.TryGetValue(args.Channel.Id, out var lastCheck)
                && DateTime.UtcNow - lastCheck < CooldownThreshold)
                return;

            if (args.Author.IsSmartlisted(c, args.Guild))
                return;
#endif

            var matches = GameNameStatusMention1.Matches(args.Message.Content);
            if (!matches.Any())
                return;

            var gameTitle = matches.Select(m => m.Groups["game_title_1"].Value)
                .Concat(matches.Select(m => m.Groups["game_title_2"].Value))
                .Concat(matches.Select(m => m.Groups["game_title_3"].Value))
                .FirstOrDefault(t => !string.IsNullOrEmpty(t));
            if (string.IsNullOrEmpty(gameTitle) || gameTitle.Length < 2)
                return;

            gameTitle = CompatList.FixGameTitleSearch(gameTitle);
            if (gameTitle.Length < 4)
                return;

            if (ProductCodeLookup.ProductCode.IsMatch(args.Message.Content))
                return;

            var (_, info) = await LookupGameAsync(args.Channel, args.Message, gameTitle).ConfigureAwait(false);
            if (string.IsNullOrEmpty(info?.Status))
                return;

            gameTitle = info.Title?.StripMarks();
            if (string.IsNullOrEmpty(gameTitle))
                return;

            var botSpamChannel = await c.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
            var status = info.Status.ToLowerInvariant();
            string msg;
            if (status == "unknown")
                msg = $"{args.Message.Author.Mention} {gameTitle} status is {status}";
            else
            {
                if (status != "playable")
                    status += " (not playable)";
                msg = $"{args.Message.Author.Mention} {gameTitle} is {status}";
                if (!string.IsNullOrEmpty(info.Date))
                      msg += $" since {info.ToUpdated()}";
            }
            msg += $"\nfor more results please use compatibility list (<https://rpcs3.net/compatibility>) or `{Config.CommandPrefix}c` command in {botSpamChannel.Mention} (`!c {gameTitle.Sanitize()}`)";
            await args.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            CooldownBuckets[args.Channel.Id] = DateTime.UtcNow;
        }

        public static async Task<(string? productCode, TitleInfo? info)> LookupGameAsync(DiscordChannel channel, DiscordMessage message, string gameTitle)
        {
            var lastBotMessages = await channel.GetMessagesBeforeAsync(message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
            foreach (var msg in lastBotMessages)
                if (BotReactionsHandler.NeedToSilence(msg).needToChill)
                    return (null, null);

            try
            {
                var requestBuilder = RequestBuilder.Start().SetSearch(gameTitle);
                var searchCompatListTask = Client.GetCompatResultAsync(requestBuilder, Config.Cts.Token);
                var localList = CompatList.GetLocalCompatResult(requestBuilder);
                var status = await searchCompatListTask.ConfigureAwait(false);
                status = status?.Append(localList);
                if (status is null
                    || status.ReturnCode != 0 && status.ReturnCode != 2
                    || !status.Results.Any())
                    return (null, null);
                
                var sortedList = status.GetSortedList();
                var bestMatch = sortedList.First();
                var listWithStatus = sortedList
                    .TakeWhile(i => Math.Abs(i.score - bestMatch.score) < double.Epsilon)
                    .Where(i => !string.IsNullOrEmpty(i.info.Status) && i.info.Status != "Unknown")
                    .ToList();
                if (listWithStatus.Count > 0)
                    bestMatch = listWithStatus.First();
                var (code, info, score) = bestMatch;
                Config.Log.Debug($"Looked up \"{gameTitle}\", got \"{info?.Title}\" with score {score}");
                if (score < Config.GameTitleMatchThreshold)
                    return (null, null);

                if (!string.IsNullOrEmpty(info?.Title))
                {
                    StatsStorage.GameStatCache.TryGetValue(info.Title, out int stat);
                    StatsStorage.GameStatCache.Set(info.Title, ++stat, StatsStorage.CacheTime);
                }

                return (code, info);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                return (null, null);
            }
        }
    }
}
