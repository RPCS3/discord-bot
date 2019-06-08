using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using PsnClient;
using PsnClient.POCOs;

namespace CompatBot.Commands
{
    [Group("psn")]
    [Description("Commands related to PSN metadata")]
    internal sealed partial class Psn: BaseCommandModuleCustom
    {
        private static readonly Client Client = new Client();
        private static readonly DiscordColor PsnBlue = new DiscordColor(0x0071cd);

        [Command("fix"), RequiresBotModRole]
        [Description("Reset thumbnail cache for specified product")]
        public async Task Fix(CommandContext ctx, [Description("Product ID to reset")] string productId)
        {
            var linksToRemove = new List<(string contentId, string link)>();
            using (var db = new ThumbnailDb())
            {
                var items = db.Thumbnail.Where(i => i.ProductCode == productId && !string.IsNullOrEmpty(i.EmbeddableUrl));
                foreach (var thumb in items)
                {
                    linksToRemove.Add((thumb.ContentId, thumb.EmbeddableUrl));
                    thumb.EmbeddableUrl = null;
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            await TryDeleteThumbnailCache(ctx, linksToRemove).ConfigureAwait(false);
            await ctx.RespondAsync($"Removed {linksToRemove.Count} cached links").ConfigureAwait(false);
        }

        [Command("rescan"), RequiresBotModRole]
        [Description("Forces a full PSN rescan")]
        public async Task Rescan(CommandContext ctx)
        {
            using (var db = new ThumbnailDb())
            {
                var items = db.State.ToList();
                foreach (var state in items)
                    state.Timestamp = 0;
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            await ctx.ReactWithAsync(Config.Reactions.Success, "Reset state timestamps").ConfigureAwait(false);
        }

        [Command("search")]
        [Description("Provides game information from PSN")]
        public Task Search(CommandContext ctx, [Description("Maximum results to return across all regions")] int maxResults, [RemainingText] string search)
        {
            if (maxResults < 1)
                maxResults = 1;
            if (ctx.Channel.IsPrivate)
            {
                if (maxResults > 50)
                    maxResults = 50;
            }
            else
            {
                if (maxResults > 15)
                    maxResults = 15;
            }
            return SearchForGame(ctx, search, maxResults);
        }

        [Command("search")]
        public Task Search(CommandContext ctx, [RemainingText] string search)
            => SearchForGame(ctx, search, 10);

        public static async Task SearchForGame(CommandContext ctx, string search, int maxResults)
        {
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            DiscordMessage msg = null;
            try
            {
                if (string.IsNullOrEmpty(search))
                {
                    var interact = ctx.Client.GetInteractivity();
                    msg = await msg.UpdateOrCreateMessageAsync(ch, "What game are you looking for?").ConfigureAwait(false);
                    var response = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ch).ConfigureAwait(false);
                    await msg.DeleteAsync().ConfigureAwait(false);
                    msg = null;
                    if (string.IsNullOrEmpty(response.Result?.Content))
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                        return;
                    }

                    search = response.Result.Content;
                }

                string titleId = null;
                var productIds = ProductCodeLookup.GetProductIds(search);
                if (productIds.Count > 0)
                {
                    using (var db = new ThumbnailDb())
                    {
                        var contentId = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productIds[0].ToUpperInvariant()).ConfigureAwait(false);
                        if (contentId?.ContentId != null)
                            titleId = contentId.ContentId;
                        if (contentId?.Name != null)
                            search = contentId.Name;
                    }
                }

                var alteredSearch = search.Trim();
                if (alteredSearch.EndsWith("demo", StringComparison.InvariantCultureIgnoreCase))
                    alteredSearch = alteredSearch.Substring(0, alteredSearch.Length - 4).TrimEnd();
                if (alteredSearch.EndsWith("trial", StringComparison.InvariantCultureIgnoreCase))
                    alteredSearch = alteredSearch.Substring(0, alteredSearch.Length - 5).TrimEnd();
                if (alteredSearch.EndsWith("体験版"))
                    alteredSearch = alteredSearch.Substring(0, alteredSearch.Length - 3).TrimEnd();

                var msgTask = msg.UpdateOrCreateMessageAsync(ch, "⏳ Searching...");
                var psnResponseUSTask = titleId == null ? Client.SearchAsync("en-US", alteredSearch, Config.Cts.Token) : Client.ResolveContentAsync("en-US", titleId, 1, Config.Cts.Token);
                var psnResponseEUTask = titleId == null ? Client.SearchAsync("en-GB", alteredSearch, Config.Cts.Token) : Client.ResolveContentAsync("en-GB", titleId, 1, Config.Cts.Token);
                var psnResponseJPTask = titleId == null ? Client.SearchAsync("ja-JP", alteredSearch, Config.Cts.Token) : Client.ResolveContentAsync("ja-JP", titleId, 1, Config.Cts.Token);
                await Task.WhenAll(msgTask, psnResponseUSTask, psnResponseEUTask, psnResponseJPTask).ConfigureAwait(false);
                var responseUS = await psnResponseUSTask.ConfigureAwait(false);
                var responseEU = await psnResponseEUTask.ConfigureAwait(false);
                var responseJP = await psnResponseJPTask.ConfigureAwait(false);
                msg = await msgTask.ConfigureAwait(false);
                msg = await msg.UpdateOrCreateMessageAsync(ch, "⌛ Preparing results...").ConfigureAwait(false);
                var usGames = GetBestMatch(responseUS?.Included, search, maxResults) ?? EmptyMatch;
                var euGames = GetBestMatch(responseEU?.Included, search, maxResults) ?? EmptyMatch;
                var jpGames = GetBestMatch(responseJP?.Included, search, maxResults) ?? EmptyMatch;
                var combinedList = usGames.Select(g => (g, "US", "en-US"))
                    .Concat(euGames.Select(g => (g, "EU", "en-GB")))
                    .Concat(jpGames.Select(g => (g, "JP", "ja-JP")))
                    .ToList();
                combinedList = GetSortedList(combinedList, search, maxResults);
                var hasResults = false;
                foreach (var (g, region, locale) in combinedList)
                {
                    if (g == null)
                        continue;

                    var thumb = await ThumbnailProvider.GetThumbnailUrlWithColorAsync(ctx.Client, g.Id, PsnBlue, g.Attributes.ThumbnailUrlBase).ConfigureAwait(false);
                    string score;
                    if ((g.Attributes.StarRating?.Score ?? 0m) == 0m || (g.Attributes.StarRating?.Total ?? 0) == 0)
                        score = "N/A";
                    else
                    {
                        if (ctx.User.Id == 247291873511604224ul)
                            score = StringUtils.GetStars(g.Attributes.StarRating?.Score);
                        else
                        score = StringUtils.GetMoons(g.Attributes.StarRating?.Score);
                    score = $"{score} ({g.Attributes.StarRating?.Score} by {g.Attributes.StarRating.Total} people)";
                }
                string fileSize = null;
                if (g.Attributes.FileSize?.Value.HasValue ?? false)
                    {
                        fileSize = g.Attributes.FileSize.Value.ToString();
                        if (g.Attributes.FileSize?.Unit is string unit && !string.IsNullOrEmpty(unit))
                            fileSize += " " + unit;
                        else
                            fileSize += " GB";
                        fileSize = $" ({fileSize})";
                    }

                    //var instructions = g.Attributes.TopCategory == "disc_based_game" ? "dumping_procedure" : "software_distribution";
                    var result = new DiscordEmbedBuilder
                    {
                        Color = thumb.color,
                        Title = $"⏬ {g.Attributes.Name?.StripMarks()} [{region}]{fileSize}",
                        Url = $"https://store.playstation.com/{locale}/product/{g.Id}",
                        Description = $"Rating: {score}\n" +
                                      $"[Instructions](https://rpcs3.net/quickstart#software_distribution)",
                        ThumbnailUrl = thumb.url,
                    };
#if DEBUG
                    result.WithFooter("Test instance");
#endif
                    hasResults = true;
                    await ch.SendMessageAsync(embed: result).ConfigureAwait(false);
            }
                if (hasResults)
                    await msg.DeleteAsync().ConfigureAwait(false);
                else
                    await msg.UpdateOrCreateMessageAsync(ch, "No results").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await msg.UpdateOrCreateMessageAsync(ch, "Something has gone wrong 😩").ConfigureAwait(false);
            }
        }

        private static List<ContainerIncluded> EmptyMatch { get; } = new List<ContainerIncluded>(0);

        private static List<(ContainerIncluded g, string, string)> GetSortedList(List<(ContainerIncluded g, string, string)> games, string search, int maxResults)
        {
            var result = (
                from i in games
                let m = new { score = search.GetFuzzyCoefficientCached(i.g.Attributes.Name), item = i }
                where m.score > 0.3 || (i.g.Attributes.Name?.StartsWith(search, StringComparison.InvariantCultureIgnoreCase) ?? false)
                orderby m.score descending
                select m.item
            ).Take(maxResults).ToList();
            if (result.Any())
                return result;

            result = games.Where(i => i.g.Type == "game").Take(maxResults).ToList();
            return result.Any() ? result : games.Take(maxResults).ToList();
        }

        private static List<ContainerIncluded> GetBestMatch(ContainerIncluded[] included, string search, int maxResults)
        {
            if (included == null)
                return null;

            var includeDemos = search.Contains("demo", StringComparison.InvariantCultureIgnoreCase)
                               || search.Contains("trial", StringComparison.InvariantCultureIgnoreCase)
                               || search.Contains("体験版");

            return (
                from i in included
                where (i.Type == "game"
                       || i.Type == "legacy-sku"
                       || (i.Type == "game-related" && i.Attributes.TopCategory == "disc_based_game")
                       )
                      && (includeDemos || (i.Attributes.TopCategory != "demo" && i.Attributes.GameContentType != "Demo") )
                      && i.Attributes.Name != null
                      && i.Attributes.ThumbnailUrlBase != null
                select i
            ).ToList();
        }

        private static async Task TryDeleteThumbnailCache(CommandContext ctx, List<(string contentId, string link)> linksToRemove)
        {
            var contentIds = linksToRemove.ToDictionary(l => l.contentId, l => l.link);
            try
            {
                var channel = await ctx.Client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                var messages = await channel.GetMessagesAsync(1000).ConfigureAwait(false);
                foreach (var msg in messages)
                    if (contentIds.TryGetValue(msg.Content, out var lnk) && msg.Attachments.Any(a => a.Url == lnk))
                    {
                        try
                        {
                            await msg.DeleteAsync().ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            Config.Log.Warn(e, "Couldn't delete cached thumbnail image");
                        }
                    }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }
    }
}
