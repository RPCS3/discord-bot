using CompatBot.Database;
using DSharpPlus.Commands.Processors.TextCommands;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class Bot
{
    [Command("import")]
    internal static class Import
    {
        [Command("metacritic"), LimitedToSpamChannel]
        [Description("Import Metacritic database dump and link it to existing PSN items")]
        public static async ValueTask ImportMc(TextCommandContext ctx)
        {
            if (await ImportLockObj.WaitAsync(0).ConfigureAwait(false))
                try
                {
                    await CompatList.ImportMetacriticScoresAsync().ConfigureAwait(false);
                    await using var db = ThumbnailDb.OpenRead();
                    var linkedItems = await db.Thumbnail.CountAsync(i => i.MetacriticId != null).ConfigureAwait(false);
                    await ctx.Channel.SendMessageAsync($"Importing Metacritic info was successful, linked {linkedItems} items").ConfigureAwait(false);
                }
                finally
                {
                    ImportLockObj.Release();
                }
            else
                await ctx.Channel.SendMessageAsync("Another import operation is already in progress").ConfigureAwait(false);
        }
    }
}