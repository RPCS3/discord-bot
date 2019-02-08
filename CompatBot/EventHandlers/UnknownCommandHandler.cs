using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Utils;
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

            var term = e.Context.Message.Content.Substring(pos).Trim().ToLowerInvariant();
            var lookup = await Explain.LookupTerm(term).ConfigureAwait(false);
            if (lookup.score < 0.5 || lookup.explanation == null)
            {
                await e.Context.RespondAsync($"Please use `{Config.CommandPrefix}help` to look at bot commands or `{Config.CommandPrefix}explain list` to look at the list of available explanations").ConfigureAwait(false);
                return;
            }

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
        }
    }
}
