using System.Text.RegularExpressions;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatBot.Database.Providers;
using CompatBot.Utils.ResultFormatters;

namespace CompatBot.EventHandlers;

internal static partial class ProductCodeLookup
{
    // see http://www.psdevwiki.com/ps3/Productcode
    [GeneratedRegex(@"(?<letters>(?:[BPSUVX][CL]|P[ETU]|NP)[AEHJKPUIX][ABDJKLMOPQRSTX]|MRTC)[ \-]?(?<numbers>\d{5})", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-GB")]
    public static partial Regex Pattern();
    private static readonly Client CompatClient = new();

    public static async Task OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args)
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
    
    public static async ValueTask LookupAndPostProductCodeEmbedAsync(DiscordClient client, DiscordMessage message, DiscordChannel channel, List<string> codesToLookup)
    {
        await message.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
        try
        {
            var formattedResults = await LookupProductCodeAndFormatAsync(client, codesToLookup).ConfigureAwait(false);
            foreach (var result in formattedResults)
                try
                {
                    var messageBuilder = new DiscordMessageBuilder().AddEmbed(result.builder);
                    //todo: pass author from context and update OnCheckUpdatesButtonClick in psn check updates
                    if (message is {Author: not null} && channel.IsSpamChannel())
                        messageBuilder.AddComponents(
                            new DiscordButtonComponent(
                                DiscordButtonStyle.Secondary,
                                $"{GlobalButtonHandler.ReplaceWithUpdatesPrefix}{result.code}",
                                "How to check for updates",
                                emoji: new(DiscordEmoji.FromUnicode("ℹ️"))
                            )
                        );
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

    internal static async ValueTask<List<(string code, DiscordEmbedBuilder builder)>> LookupProductCodeAndFormatAsync(DiscordClient client, List<string> codesToLookup)
    {
        var results = codesToLookup.Select(code => (code, client.LookupGameInfoAsync(code))).ToList();
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
        return formattedResults.DistinctBy(e => e.builder.Title).ToList();
    }

    public static List<string> GetProductIds(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return [];

        return Pattern().Matches(input)
            .Select(match => (match.Groups["letters"].Value + match.Groups["numbers"]).ToUpper())
            .Distinct()
            .ToList();
    }

    public static async Task<DiscordEmbedBuilder> LookupGameInfoAsync(this DiscordClient client, string? code, string? gameTitle = null, bool forLog = false, string? category = null)
        => (await LookupGameInfoWithEmbedAsync(client, code, gameTitle, forLog, category).ConfigureAwait(false)).embedBuilder;
        
    public static async ValueTask<(DiscordEmbedBuilder embedBuilder, CompatResult? compatResult)> LookupGameInfoWithEmbedAsync(this DiscordClient client, string? code, string? gameTitle = null, bool forLog = false, string? category = null)
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
}
