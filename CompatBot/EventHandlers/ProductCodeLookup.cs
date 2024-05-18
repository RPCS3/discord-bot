using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatBot.Commands.Attributes;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers;

internal static partial class ProductCodeLookup
{
    // see http://www.psdevwiki.com/ps3/Productcode
    [GeneratedRegex(@"(?<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][ABDJKLMPQRSTX]|MRTC)[ \-]?(?<numbers>\d{5})", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-GB")]
    public static partial Regex Pattern();
    private static readonly Client CompatClient = new();

    public static async Task OnMessageCreated(DiscordClient c, MessageCreateEventArgs args)
    {
        if (DefaultHandlerFilter.IsFluff(args.Message))
            return;

        var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
        if (lastBotMessages.Any(msg => BotReactionsHandler.NeedToSilence(msg).needToChill))
            return;

        lastBotMessages = await args.Channel.GetMessagesBeforeCachedAsync(args.Message.Id, Config.ProductCodeLookupHistoryThrottle).ConfigureAwait(false);
        StringBuilder? previousRepliesBuilder = null;
        foreach (var msg in lastBotMessages.Where(m => m.Author.IsCurrent))
        {
            previousRepliesBuilder ??= new StringBuilder();
            previousRepliesBuilder.AppendLine(msg.Content);
            var embeds = msg.Embeds;
            if (embeds?.Count > 0)
                foreach (var embed in embeds)
                    previousRepliesBuilder.AppendLine(embed.Title).AppendLine(embed.Description);
        }
        var previousReplies = previousRepliesBuilder?.ToString() ?? "";

        var codesToLookup = GetProductIds(args.Message.Content)
            .Where(pc => !previousReplies.Contains(pc, StringComparison.InvariantCultureIgnoreCase))
            .Take(args.Channel.IsPrivate ? 50 : 5)
            .ToList();
        if (codesToLookup.Count == 0)
            return;

        await LookupAndPostProductCodeEmbedAsync(c, args.Message, args.Channel, codesToLookup).ConfigureAwait(false);
    }

    public static async Task LookupAndPostProductCodeEmbedAsync(DiscordClient client, DiscordMessage message, DiscordChannel channel, List<string> codesToLookup)
    {
        await message.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
        try
        {
            var results = new List<(string code, Task<DiscordEmbedBuilder> task)>(codesToLookup.Count);
            foreach (var code in codesToLookup)
                results.Add((code, client.LookupGameInfoAsync(code)));
            var formattedResults = new List<(string code, DiscordEmbedBuilder builder)>(results.Count);
            foreach (var (code, task) in results)
                try
                {
                    formattedResults.Add((code, await task.ConfigureAwait(false)));
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Couldn't get product code info for {code}");
                }

            // get only results with unique titles
            formattedResults = formattedResults.DistinctBy(e => e.builder.Title).ToList();
            var lookupEmoji = new DiscordComponentEmoji(DiscordEmoji.FromUnicode("🔍"));
            foreach (var result in formattedResults)
                try
                {
                    await FixAfrikaAsync(client, message, result.builder).ConfigureAwait(false);
                    var messageBuilder = new DiscordMessageBuilder().WithEmbed(result.builder);
                    if (LimitedToSpamChannel.IsSpamChannel(channel))
                        messageBuilder.AddComponents(new DiscordButtonComponent(ButtonStyle.Secondary, $"replace with game updates:{message.Author.Id}:{message.Id}:{result.code}", "Check for updates", emoji: lookupEmoji));
                    await DiscordMessageExtensions.UpdateOrCreateMessageAsync(null, channel, messageBuilder).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Couldn't post result for {result.code} ({result.builder.Title})");
                }
        }
        finally
        {
            await message.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
        }
    }

    public static List<string> GetProductIds(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return new(0);

        return Pattern().Matches(input)
            .Select(match => (match.Groups["letters"].Value + match.Groups["numbers"]).ToUpper())
            .Distinct()
            .ToList();
    }

    public static async Task<DiscordEmbedBuilder> LookupGameInfoAsync(this DiscordClient client, string? code, string? gameTitle = null, bool forLog = false, string? category = null)
        => (await LookupGameInfoWithEmbedAsync(client, code, gameTitle, forLog, category).ConfigureAwait(false)).embedBuilder;
        
    public static async Task<(DiscordEmbedBuilder embedBuilder, CompatResult? compatResult)> LookupGameInfoWithEmbedAsync(this DiscordClient client, string? code, string? gameTitle = null, bool forLog = false, string? category = null)
    {
        if (string.IsNullOrEmpty(code))
            return (TitleInfo.Unknown.AsEmbed(code, gameTitle, forLog), null);

        string? thumbnailUrl = null;
        CompatResult? result = null;
        try
        {
            result = await CompatClient.GetCompatResultAsync(RequestBuilder.Start().SetSearch(code), Config.Cts.Token).ConfigureAwait(false);
            if (result?.ReturnCode == -2)
                return (TitleInfo.Maintenance.AsEmbed(code), result);

            if (result?.ReturnCode == -1)
                return (TitleInfo.CommunicationError.AsEmbed(code), result);

            thumbnailUrl = await client.GetThumbnailUrlAsync(code).ConfigureAwait(false);

            if (result?.Results != null && result.Results.TryGetValue(code, out var info))
                return (info.AsEmbed(code, gameTitle, forLog, thumbnailUrl), result);

            gameTitle ??= await ThumbnailProvider.GetTitleNameAsync(code, Config.Cts.Token).ConfigureAwait(false);
            if (category == "1P")
            {
                var ti = new TitleInfo
                {
                    Commit = "8b449ce76c91d5ff7a2829b233befe7d6df4b24f",
                    Date = "2018-06-23",
                    Pr = 4802,
                    Status = "Playable",
                };
                return (ti.AsEmbed(code, gameTitle, forLog, thumbnailUrl), result);
            }
            if (category is "2P" or "2G" or "2D" or "PP" or "PE" or "MN")
            {
                var ti = new TitleInfo
                {
                    Status = "Nothing"
                };
                return (ti.AsEmbed(code, gameTitle, forLog, thumbnailUrl), result);
            }
            return (TitleInfo.Unknown.AsEmbed(code, gameTitle, forLog, thumbnailUrl), result);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Couldn't get compat result for {code}");
            return (TitleInfo.CommunicationError.AsEmbed(code, gameTitle, forLog, thumbnailUrl), result);
        }
    }

    public static async Task FixAfrikaAsync(DiscordClient client, DiscordMessage message, DiscordEmbedBuilder titleInfoEmbed)
    {
        if (message.IsOnionLike()
            && (
                titleInfoEmbed.Title.Contains("africa", StringComparison.InvariantCultureIgnoreCase)
                || titleInfoEmbed.Title.Contains("afrika", StringComparison.InvariantCultureIgnoreCase)
            ))
        {
            var sqvat = client.GetEmoji(":sqvat:", Config.Reactions.No)!;
            titleInfoEmbed.Title = "How about no (๑•ิཬ•ั๑)";
            if (!string.IsNullOrEmpty(titleInfoEmbed.Thumbnail?.Url))
                titleInfoEmbed.WithThumbnail("https://cdn.discordapp.com/attachments/417347469521715210/516340151589535745/onionoff.png");
            await message.ReactWithAsync(sqvat).ConfigureAwait(false);
        }
    }
}
