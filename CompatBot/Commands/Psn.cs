using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using PsnClient;

namespace CompatBot.Commands;

[Command("psn")]
[Description("Commands related to PSN metadata")]
internal sealed partial class Psn
{
    private static readonly Client Client = new();

/*
    [Command("rename"), TextAlias("setname", "settitle"), RequiresBotModRole]
    [Description("Command to set or change game title for specific product code")]
    public async Task Rename(CommandContext ctx, [Description("Product code such as BLUS12345")] string productCode, [RemainingText, Description("New game title to save in the database")] string title)
    {
        productCode = productCode.ToUpperInvariant();
        await using var db = new ThumbnailDb();
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
        var productCodeMatch = ProductCodeLookup.Pattern().Match(contentId);
        var contentIdMatch = PsnScraper.ContentIdMatcher().Match(contentId);
        string productCode;
        if (contentIdMatch.Success)
        {
            productCode = contentIdMatch.Groups["product_id"].Value;
        }
        else if (productCodeMatch.Success)
        {
            productCode = productCodeMatch.Groups["letters"].Value + productCodeMatch.Groups["numbers"].Value;
            contentId = "";
        }
        else
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid content id", true).ConfigureAwait(false);
            return;
        }

        await using var db = new ThumbnailDb();
        var item = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productCode);
        if (item is null)
        {
            item = new Thumbnail
            {
                ProductCode = productCode,
                ContentId = string.IsNullOrEmpty(contentId) ? null : contentId,
                Name = title,
            };
            await db.AddAsync(item).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success, "Title added successfully").ConfigureAwait(false);
        }
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Product code {contentId} already exists", true).ConfigureAwait(false);
    }
*/
}
