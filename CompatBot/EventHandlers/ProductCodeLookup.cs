using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
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
    internal static class ProductCodeLookup
    {
        // see http://www.psdevwiki.com/ps3/Productcode
        public static readonly Regex ProductCode = new Regex(@"(?<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][ABSM]|MRTC)[ \-]?(?<numbers>\d{5})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Client CompatClient = new Client();

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

            var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
            foreach (var msg in lastBotMessages)
                if (BotReactionsHandler.NeedToSilence(msg).needToChill)
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

            await LookupAndPostProductCodeEmbedAsync(args.Client, args.Message, codesToLookup).ConfigureAwait(false);
        }

        public static async Task LookupAndPostProductCodeEmbedAsync(DiscordClient client, DiscordMessage message, List<string> codesToLookup)
        {
            await message.ReactWithAsync(client, Config.Reactions.PleaseWait).ConfigureAwait(false);
            try
            {
                var results = new List<(string code, Task<DiscordEmbedBuilder> task)>(codesToLookup.Count);
                foreach (var code in codesToLookup)
                    results.Add((code, client.LookupGameInfoAsync(code)));
                var formattedResults = new List<DiscordEmbedBuilder>(results.Count);
                foreach (var result in results)
                    try
                    {
                        formattedResults.Add(await result.task.ConfigureAwait(false));
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Couldn't get product code info for {result.code}");
                    }

                // get only results with unique titles
                formattedResults = formattedResults.GroupBy(e => e.Title).Select(g => g.First()).ToList();
                DiscordEmoji sqvat = null;
                foreach (var result in formattedResults)
                    try
                    {
                        await FixAfrikaAsync(client, message, result).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync(embed: result).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Couldn't post result for {result.Title}");
                    }
            }
            finally
            {
                await message.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }

        public static List<string> GetProductIds(string input)
        {
            if (string.IsNullOrEmpty(input))
                return new List<string>(0);

            return ProductCode.Matches(input)
                .Select(match => (match.Groups["letters"].Value + match.Groups["numbers"]).ToUpper())
                .Distinct()
                .ToList();
        }

        public static async Task<DiscordEmbedBuilder> LookupGameInfoAsync(this DiscordClient client, string code, string gameTitle = null, bool forLog = false)
        {
            if (string.IsNullOrEmpty(code))
                return TitleInfo.Unknown.AsEmbed(code, gameTitle, forLog);

            try
            {
                var result = await CompatClient.GetCompatResultAsync(RequestBuilder.Start().SetSearch(code), Config.Cts.Token).ConfigureAwait(false);
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

        public static async Task FixAfrikaAsync(DiscordClient client, DiscordMessage message, DiscordEmbedBuilder titleInfoEmbed)
        {
            if (!message.Channel.IsPrivate
                && message.Author.Id == 197163728867688448
                && (
                    titleInfoEmbed.Title.Contains("africa", StringComparison.InvariantCultureIgnoreCase) ||
                    titleInfoEmbed.Title.Contains("afrika", StringComparison.InvariantCultureIgnoreCase)
                ))
            {
                var sqvat = client.GetEmoji(":sqvat:", Config.Reactions.No);
                titleInfoEmbed.Title = "How about no (๑•ิཬ•ั๑)";
                if (!string.IsNullOrEmpty(titleInfoEmbed.ThumbnailUrl))
                    titleInfoEmbed.ThumbnailUrl = "https://cdn.discordapp.com/attachments/417347469521715210/516340151589535745/onionoff.png";
                await message.ReactWithAsync(client, sqvat).ConfigureAwait(false);
            }
        }
    }
}
