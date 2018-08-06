using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;
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
        public sealed class Check : BaseCommandModuleCustom
        {
            [Command("updates"), Aliases("update")]
            [Description("Checks if specified product has any updates")]
            public async Task Updates(CommandContext ctx, [RemainingText, Description("Product ID such as BLUS12345")] string productId)
            {
                productId = ProductCodeLookup.GetProductIds(productId).FirstOrDefault();
                if (string.IsNullOrEmpty(productId))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{productId.Sanitize()}` is not a valid product ID").ConfigureAwait(false);
                    return;
                }

                var updateInfo = await Client.GetTitleUpdatesAsync(productId, Config.Cts.Token).ConfigureAwait(false);
                var embed = await updateInfo.AsEmbedAsync(ctx.Client, productId).ConfigureAwait(false);
                await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
            }
        }
    }
}
