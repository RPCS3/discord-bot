using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;

namespace CompatBot.EventHandlers
{
    internal static class UnknownCommandHandler
    {
        public static async Task OnError(CommandErrorEventArgs e)
        {
            if (!(e.Exception is CommandNotFoundException cnfe))
            {
                Config.Log.Error(e.Exception);
                return;
            }

            if (string.IsNullOrEmpty(cnfe.CommandName))
                return;

            if (e.Context.Prefix != Config.CommandPrefix && e.Context.CommandsNext.RegisteredCommands.TryGetValue("8ball", out var cmd))
            {
                var updatedContext = e.Context.CommandsNext.CreateContext(
                    e.Context.Message,
                    e.Context.Prefix,
                    cmd,
                    e.Context.RawArgumentString
                );
                try { await cmd.ExecuteAsync(updatedContext).ConfigureAwait(false); } catch { }
                return;
            }

            var pos = e.Context.Message.Content.IndexOf(cnfe.CommandName);
            if (pos < 0)
                return;

            var gameTitle = e.Context.Message.Content.Substring(pos).Trim(40);
            var term = gameTitle.ToLowerInvariant();
            var lookup = await Explain.LookupTerm(term).ConfigureAwait(false);
            if (lookup.score > 0.5 && lookup.explanation != null)
            {
                if (!string.IsNullOrEmpty(lookup.fuzzyMatch))
                {
                    var fuzzyNotice = $"Showing explanation for `{lookup.fuzzyMatch}`:";
#if DEBUG
                    fuzzyNotice = $"Showing explanation for `{lookup.fuzzyMatch}` ({lookup.score:0.######}):";
#endif
                    await e.Context.RespondAsync(fuzzyNotice).ConfigureAwait(false);
                }
                var explain = lookup.explanation;
                await e.Context.Channel.SendMessageAsync(explain.Text, explain.Attachment, explain.AttachmentFilename).ConfigureAwait(false);
                return;
            }

            gameTitle = CompatList.FixGameTitleSearch(gameTitle);
            var productCodes = ProductCodeLookup.GetProductIds(gameTitle);
            if (productCodes.Any())
            {
                await ProductCodeLookup.LookupAndPostProductCodeEmbedAsync(e.Context.Client, e.Context.Message, productCodes).ConfigureAwait(false);
                return;
            }

            var (productCode, titleInfo) = await IsTheGamePlayableHandler.LookupGameAsync(e.Context.Channel, e.Context.Message, gameTitle).ConfigureAwait(false);
            if (titleInfo != null)
            {
                var thumbUrl = await e.Context.Client.GetThumbnailUrlAsync(productCode).ConfigureAwait(false);
                var embed = titleInfo.AsEmbed(productCode, thumbnailUrl: thumbUrl);
                await e.Context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                return;
            }

            var ch = await e.Context.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendMessageAsync(
                $"I am not sure what you wanted me to do, please use one of the following commands:\n" +
                $"`{Config.CommandPrefix}c {term.Sanitize()}` to check the game status\n" +
                $"`{Config.CommandPrefix}explain list` to look at the list of available explanations\n" +
                $"`{Config.CommandPrefix}help` to look at available bot commands\n"
            ).ConfigureAwait(false);
        }
    }
}
