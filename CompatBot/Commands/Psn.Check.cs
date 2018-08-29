using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using PsnClient;

namespace CompatBot.Commands
{
    internal sealed partial class Psn
    {
        private static readonly Client Client = new Client();

        [Group("check")]
        [Description("Commands to check for various stuff on PSN")]
        public sealed class Check: BaseCommandModuleCustom
        {
            [Command("updates"), Aliases("update")]
            [Description("Checks if specified product has any updates")]
            public async Task Updates(CommandContext ctx, [RemainingText, Description("Product ID such as `BLUS12345`")] string productId)
            {
                productId = ProductCodeLookup.GetProductIds(productId).FirstOrDefault();
                if (string.IsNullOrEmpty(productId))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{productId.Sanitize()}` is not a valid product ID").ConfigureAwait(false);
                    return;
                }

                var updateInfo = await Client.GetTitleUpdatesAsync(productId, Config.Cts.Token).ConfigureAwait(false);
                var embeds = await updateInfo.AsEmbedAsync(ctx.Client, productId).ConfigureAwait(false);
                foreach (var embed in embeds)
                    await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
            }

            [Command("content")]
            [Description("Adds PSN content id to the scraping queue")]
            public async Task Content(CommandContext ctx, [RemainingText, Description("Content IDs to scrape, such as `UP0006-NPUB30592_00-MONOPOLYPSNNA000`")] string contentIds)
            {
                if (string.IsNullOrEmpty(contentIds))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "No IDs were specified").ConfigureAwait(false);
                    return;
                }

                var matches = PsnScraper.ContentIdMatcher.Matches(contentIds.ToUpperInvariant());
                var itemsToCheck = matches.Select(m => m.Groups["content_id"].Value).ToList();
                if (itemsToCheck.Count == 0)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "No IDs were specified").ConfigureAwait(false);
                    return;
                }

                foreach (var id in itemsToCheck)
                    PsnScraper.CheckContentIdAsync(ctx, id, Config.Cts.Token);

                await ctx.ReactWithAsync(Config.Reactions.Success, $"Added {itemsToCheck.Count} ID{StringUtils.GetSuffix(itemsToCheck.Count)} to the scraping queue").ConfigureAwait(false);
            }
        }
    }
}
