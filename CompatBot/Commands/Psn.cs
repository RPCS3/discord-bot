using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
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


        [Command("rename"), Aliases("setname", "settitle"), RequiresBotModRole]
        [Description("Command to set or change game title for specific product code")]
        public async Task Rename(CommandContext ctx, [Description("Product code such as BLUS12345")] string productCode, [RemainingText, Description("New game title to save in the database")] string title)
        {
            productCode = productCode.ToUpperInvariant();
            using var db = new ThumbnailDb();
            var item = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productCode);
            if (item == null) 
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Unknown product code {productCode}", true).ConfigureAwait(false);
            else
            {
                item.Name = title;
                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, "Title updated successfully").ConfigureAwait(false);
            }
        }

        [Command("add"), RequiresBotModRole]
        [Description("Add new product code with specified title to the bot database")]
        public async Task Add(CommandContext ctx, [Description("Product code such as BLUS12345")] string contentId, [RemainingText, Description("New game title to save in the database")] string title)
        {
            contentId = contentId.ToUpperInvariant();
            var productCode = contentId;
            var productCodeMatch = ProductCodeLookup.ProductCode.Match(contentId);
            var contentIdMatch = PsnScraper.ContentIdMatcher.Match(contentId);
            if (contentIdMatch.Success)
            {
                productCode = contentIdMatch.Groups["product_id"].Value;
            }
            else if (productCodeMatch.Success)
            {
                productCode = productCodeMatch.Groups["letters"].Value + productCodeMatch.Groups["numbers"].Value;
                contentId = null;
            }
            else
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid content id", true).ConfigureAwait(false);
                return;
            }
            
            using var db = new ThumbnailDb();
            var item = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productCode);
            if (item != null) 
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Product code {contentId} already exists", true).ConfigureAwait(false);
            else
            {
                item = new Thumbnail
                {
                    ProductCode = contentId,
                    ContentId = contentId,
                    Name = title,
                };
                db.Add(item);
                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, "Title added successfully").ConfigureAwait(false);
            }
        }
    }
}
