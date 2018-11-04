using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using IrdLibraryClient;

namespace CompatBot.Commands
{
    internal sealed class Ird: BaseCommandModuleCustom
    {
        private static readonly IrdClient Client = new IrdClient();

        [Command("ird"), TriggersTyping]
        [Description("Searches IRD Library for the matching .ird files")]
        public async Task Search(CommandContext ctx, [RemainingText, Description("Product code or game title to look up")] string query)
        {
            var result = await Client.SearchAsync(query, Config.Cts.Token).ConfigureAwait(false);
            var embed = result.AsEmbed();
            await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
        }
    }
}
