using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
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
        public Task Search(CommandContext ctx, [RemainingText] string search)
            => SearchForGame(ctx, search);
        
        public static async Task SearchForGame(CommandContext ctx, [RemainingText] string search)
        {
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            DiscordMessage msg = null;
            if (string.IsNullOrEmpty(search))
            {
                var interact = ctx.Client.GetInteractivity();
                msg = await msg.UpdateOrCreateMessageAsync(ch, "What game are you looking for?").ConfigureAwait(false);
                var response = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ch).ConfigureAwait(false);
                await msg.DeleteAsync().ConfigureAwait(false);
                msg = null;
                if (string.IsNullOrEmpty(response?.Message?.Content))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    return;
                }
                search = response.Message.Content;
            }
            var msgTask = msg.UpdateOrCreateMessageAsync(ch, "⏳ Searching...");
            var psnResponseUSTask = Client.SearchAsync("en-US", search, Config.Cts.Token);
            var psnResponseEUTask = Client.SearchAsync("en-GB", search, Config.Cts.Token);
            var psnResponseJPTask = Client.SearchAsync("ja-JP", search, Config.Cts.Token);
            await Task.WhenAll(msgTask, psnResponseUSTask, psnResponseEUTask, psnResponseJPTask).ConfigureAwait(false);
            var responseUS = await psnResponseUSTask.ConfigureAwait(false);
            var responseEU = await psnResponseEUTask.ConfigureAwait(false);
            var responseJP = await psnResponseJPTask.ConfigureAwait(false);
            msg = await msgTask.ConfigureAwait(false);
            msg = await msg.UpdateOrCreateMessageAsync(ch, "⌛ Preparing results...").ConfigureAwait(false);
            var usGame = GetBestMatch(responseUS.Included, search);
            var euGame = GetBestMatch(responseEU.Included, search);
            var jpGame = GetBestMatch(responseJP.Included, search);
            var hasResults = false;
            foreach (var (g, region, locale) in new[]{(usGame, "US", "en-US"), (euGame, "EU", "en-GB"), (jpGame, "JP", "ja-JP")}.Where(i => i.Item1 != null))
            {
                var thumb = await ThumbnailProvider.GetThumbnailUrlWithColorAsync(ctx.Client, g.Id, PsnBlue, g.Attributes.ThumbnailUrlBase).ConfigureAwait(false);
                var score = g.Attributes.StarRating?.Score == null ? "N/A" : $"{StringUtils.GetStars(g.Attributes.StarRating?.Score)} ({g.Attributes.StarRating?.Score} / {g.Attributes.StarRating.Total} people)";
                var result = new DiscordEmbedBuilder
                {
                    Color = thumb.color,
                    Title = $"⏬ {g.Attributes.Name} [{region}] ({g.Attributes.FileSize?.Value} {g.Attributes.FileSize?.Unit})",
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

        private static ContainerIncluded GetBestMatch(ContainerIncluded[] included, string search)
        {
            return (
                from i in included
                where (i.Type == "game" || i.Type == "legacy-sku") && (i.Attributes.TopCategory != "demo" && i.Attributes.GameContentType != "Demo")
                let m = new {score = search.GetFuzzyCoefficientCached(i.Attributes.Name), item = i}
                where m.score > 0.3 || (i.Attributes.Name?.StartsWith(search, StringComparison.InvariantCultureIgnoreCase) ?? false)
                orderby m.score descending
                select m.item
            ).FirstOrDefault();
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
