using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;

namespace CompatBot.Utils
{
    public static class InteractivityExtensions
    {
        public static Task<(DiscordMessage message, MessageContext text, ReactionContext reaction)> WaitForMessageOrReactionAsync(
            this InteractivityExtension interactivity,
            DiscordMessage message,
            DiscordUser user,
            params DiscordEmoji[] reactions)
            => WaitForMessageOrReactionAsync(interactivity, message, user, null, reactions);

        public static async Task<(DiscordMessage message, MessageContext text, ReactionContext reaction)> WaitForMessageOrReactionAsync(
            this InteractivityExtension interactivity,
            DiscordMessage message,
            DiscordUser user,
            TimeSpan? timeout,
            params DiscordEmoji[] reactions)
        {
            if (reactions.Length == 0)
                throw new ArgumentException("At least one reaction must be specified", nameof(reactions));

            reactions = reactions.Where(r => r != null).ToArray();
            foreach (var emoji in reactions)
                await message.ReactWithAsync(interactivity.Client, emoji).ConfigureAwait(false);
            var waitTextResponseTask = interactivity.WaitForMessageAsync(m => m.Author == user && !string.IsNullOrEmpty(m.Content), timeout);
            var waitReactionResponse = interactivity.WaitForMessageReactionAsync(reactions.Contains, message, user, timeout);
            await Task.WhenAny(
                waitTextResponseTask,
                waitReactionResponse
            ).ConfigureAwait(false);
            try
            {
                await message.DeleteAllReactionsAsync().ConfigureAwait(false);
            }
            catch
            {
                await message.DeleteAsync().ConfigureAwait(false);
                message = null;
            }
            MessageContext text = null;
            ReactionContext reaction = null;
            if (waitTextResponseTask.IsCompletedSuccessfully)
                text = await waitTextResponseTask;
            if (waitReactionResponse.IsCompletedSuccessfully)
                reaction = await waitReactionResponse;
            if (text != null)
                try { await text.Message.DeleteAsync().ConfigureAwait(false); } catch { }
            return (message, text, reaction);
        }
    }
}
