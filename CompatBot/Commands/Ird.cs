using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database.Providers;
using CompatBot.Utils.ResultFormatters;
using IrdLibraryClient;

namespace CompatBot.Commands;

internal static class Ird
{
    private static readonly IrdClient Client = new();

    [Command("ird")]
    [Description("Search IRD Library")]
    public static async ValueTask Search(
        SlashCommandContext ctx,
        [Description("Product code or game title"), MinMaxLength(3)]
        [SlashAutoCompleteProvider<ProductCodeAutoCompleteProvider>]
        string query
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ModProvider.IsMod(ctx.User.Id);
        var result = await Client.SearchAsync(query, Config.Cts.Token).ConfigureAwait(false);
        await ctx.RespondAsync(embed: result.AsEmbed(), ephemeral: ephemeral).ConfigureAwait(false);
    }
}

