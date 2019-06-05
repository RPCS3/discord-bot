using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers
{
    internal static class UnknownCommandHandler
    {
        public static async Task OnError(CommandErrorEventArgs e)
        {
            if (e.Context.User.IsBotSafeCheck())
                return;

            if (!(e.Exception is CommandNotFoundException cnfe))
            {
                Config.Log.Error(e.Exception);
                return;
            }

            if (string.IsNullOrEmpty(cnfe.CommandName))
                return;

            if (e.Context.Prefix != Config.CommandPrefix
                && e.Context.Prefix != Config.AutoRemoveCommandPrefix
                && (e.Context.Message.Content?.EndsWith("?") ?? false)
                && e.Context.CommandsNext.RegisteredCommands.TryGetValue("8ball", out var cmd))
            {
                var updatedContext = e.Context.CommandsNext.CreateContext(
                    e.Context.Message,
                    e.Context.Prefix,
                    cmd,
                    e.Context.Message.Content.Substring(e.Context.Prefix.Length).Trim()
                );
                try { await cmd.ExecuteAsync(updatedContext).ConfigureAwait(false); } catch { }
                return;
            }

            if (cnfe.CommandName.Length < 3)
                return;

#if !DEBUG
            if (e.Context.User.IsSmartlisted(e.Context.Client, e.Context.Guild))
                return;
#endif

            var pos = e.Context.Message?.Content?.IndexOf(cnfe.CommandName) ?? -1;
            if (pos < 0)
                return;

            var gameTitle = e.Context.Message.Content.Substring(pos).TrimEager().Trim(40);
            if (string.IsNullOrEmpty(gameTitle) || char.IsPunctuation(gameTitle[0]))
                return;

            var term = gameTitle.ToLowerInvariant();
            var (explanation, fuzzyMatch, score) = await Explain.LookupTerm(term).ConfigureAwait(false);
            if (score > 0.5 && explanation != null)
            {
                if (!string.IsNullOrEmpty(fuzzyMatch))
                {
                    var fuzzyNotice = $"Showing explanation for `{fuzzyMatch}`:";
#if DEBUG
                    fuzzyNotice = $"Showing explanation for `{fuzzyMatch}` ({score:0.######}):";
#endif
                    await e.Context.RespondAsync(fuzzyNotice).ConfigureAwait(false);
                }
                StatsStorage.ExplainStatCache.TryGetValue(explanation.Keyword, out int stat);
                StatsStorage.ExplainStatCache.Set(explanation.Keyword, ++stat, StatsStorage.CacheTime);
                await e.Context.Channel.SendMessageAsync(explanation.Text, explanation.Attachment, explanation.AttachmentFilename).ConfigureAwait(false);
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
                await ProductCodeLookup.FixAfrikaAsync(e.Context.Client, e.Context.Message, embed).ConfigureAwait(false);
                await e.Context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                return;
            }

            var ch = await e.Context.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendMessageAsync(
                $"I am not sure what you wanted me to do, please use one of the following commands:\n" +
                $"`{Config.CommandPrefix}c {term.Replace('`', '\'').Sanitize()}` to check the game status\n" +
                $"`{Config.CommandPrefix}explain list` to look at the list of available explanations\n" +
                $"`{Config.CommandPrefix}help` to look at available bot commands\n"
            ).ConfigureAwait(false);
        }
    }
}
