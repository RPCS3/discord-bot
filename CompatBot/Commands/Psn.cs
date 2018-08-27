using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    [Group("psn")]
    [Description("Commands related to PSN metadata")]
    internal sealed partial class Psn: BaseCommandModuleCustom
    {
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
                            ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", "Couldn't delete cached thumbnail image: " + e, DateTime.Now);
                        }
                    }
            }
            catch (Exception e)
            {
                ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", e.ToString(), DateTime.Now);
            }
        }
    }
}
