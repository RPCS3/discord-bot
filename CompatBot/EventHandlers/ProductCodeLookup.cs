﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class ProductCodeLookup
    {
        // see http://www.psdevwiki.com/ps3/Productcode
        public static readonly Regex ProductCode = new Regex(@"(?<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][ABSM]|MRTC)[ \-]?(?<numbers>\d{5})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Client compatClient = new Client();

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

            var codesToLookup = GetProductIds(args.Message.Content)
                .Where(c => !previousReplies.Contains(c, StringComparison.InvariantCultureIgnoreCase))
                .Take(args.Channel.IsPrivate ? 50 : 5)
                .ToList();
            if (codesToLookup.Count == 0)
                return;

            await args.Channel.TriggerTypingAsync().ConfigureAwait(false);
            var results = new List<(string code, Task<DiscordEmbed> task)>(codesToLookup.Count);
            foreach (var code in codesToLookup)
                results.Add((code, args.Client.LookupGameInfoAsync(code)));
            foreach (var result in results)
                try
                {
                    await args.Channel.SendMessageAsync(embed: await result.task.ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Couldn't post result for {result.code}");
                }
        }

        public static List<string> GetProductIds(string input)
        {
            return ProductCode.Matches(input)
                .Select(match => (match.Groups["letters"].Value + match.Groups["numbers"]).ToUpper())
                .Distinct()
                .ToList();
        }

        public static async Task<DiscordEmbed> LookupGameInfoAsync(this DiscordClient client, string code, string gameTitle = null, bool forLog = false)
        {
            if (string.IsNullOrEmpty(code))
                return TitleInfo.Unknown.AsEmbed(code);

            try
            {
                var result = await compatClient.GetCompatResultAsync(RequestBuilder.Start().SetSearch(code), Config.Cts.Token).ConfigureAwait(false);
                if (result?.ReturnCode == -2)
                    return TitleInfo.Maintenance.AsEmbed(null);

                if (result?.ReturnCode == -1)
                    return TitleInfo.CommunicationError.AsEmbed(null);

                var thumbnailUrl = await client.GetThumbnailUrlAsync(code).ConfigureAwait(false);
                if (result?.Results == null)
                    return TitleInfo.Unknown.AsEmbed(code, gameTitle, forLog, thumbnailUrl);

                if (result.Results.TryGetValue(code, out var info))
                    return info.AsEmbed(code, gameTitle, forLog, thumbnailUrl);

                return TitleInfo.Unknown.AsEmbed(code, gameTitle, forLog, thumbnailUrl);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't get compat result for {code}");
                return TitleInfo.CommunicationError.AsEmbed(null);
            }
        }
    }
}
