using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using Microsoft.EntityFrameworkCore;
using PsnClient;

namespace CompatBot.Commands;

[Command("psn"), AllowDMUsage]
[Description("Commands related to PSN metadata")]
internal static partial class Psn
{
    private static readonly Client Client = new();

    [Command("rename"), RequiresBotModRole]
    [Description("Change game title for specific product code in bot's PSN database")]
    public static async ValueTask Rename(
        SlashCommandContext ctx,
        [Description("Product code such as `BLUS12345`"), MinMaxLength(9, 9), SlashAutoCompleteProvider<ProductCodeAutoCompleteProvider>]
        string productCode,
        [Description("New game title to save in the database")]
        string title
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        productCode = productCode.ToUpperInvariant();
        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        var item = wdb.Thumbnail.AsNoTracking().FirstOrDefault(t => t.ProductCode == productCode);
        if (item is null)
            await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown product code {productCode}", ephemeral: true).ConfigureAwait(false);
        else
        {
            item.Name = title;
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} Title updated successfully", ephemeral: ephemeral).ConfigureAwait(false);
        }
    }

    [Command("add"), RequiresBotModRole]
    [Description("Add new product code with specified title to the bot database")]
    public static async ValueTask Add(
        SlashCommandContext ctx,
        [Description("Product code (e.g. `BLUS12345`) or PSN content ID (e.g.`GP0002-BLUS30219_00-MADAGASCARPARENT`")]
        [MinMaxLength(9, 36)]
        string contentId,
        [Description("Game title to save in the database")]
        string title)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
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
            await ctx.RespondAsync($"{Config.Reactions.Failure} Invalid content id", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        var item = wdb.Thumbnail.AsNoTracking().FirstOrDefault(t => t.ProductCode == productCode);
        if (item is null)
        {
            item = new()
            {
                ProductCode = productCode,
                ContentId = contentId is {Length: >0} ? contentId : null,
                Name = title,
            };
            await wdb.AddAsync(item).ConfigureAwait(false);
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} Title added successfully", ephemeral: ephemeral).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Product code {contentId} already exists", ephemeral: true).ConfigureAwait(false);
    }
}
