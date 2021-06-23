using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.EventHandlers;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils
{
    public static class InteractivityExtensions
    {
        public static Task<(DiscordMessage? message, DiscordMessage? text, MessageReactionAddEventArgs? reaction)> WaitForMessageOrReactionAsync(
            this InteractivityExtension interactivity,
            DiscordMessage message,
            DiscordUser user,
            params DiscordEmoji?[] reactions)
            => WaitForMessageOrReactionAsync(interactivity, message, user, null, reactions);

        public static async Task<(DiscordMessage? message, DiscordMessage? text, MessageReactionAddEventArgs? reaction)> WaitForMessageOrReactionAsync(
            this InteractivityExtension interactivity,
            DiscordMessage message,
            DiscordUser user,
            TimeSpan? timeout,
            params DiscordEmoji?[] reactions)
        {
            if (reactions.Length == 0)
                throw new ArgumentException("At least one reaction must be specified", nameof(reactions));

            DiscordMessage? result = message;
            try
            {
                reactions = reactions.Where(r => r != null).ToArray();
                foreach (var emoji in reactions)
                    await message.ReactWithAsync(emoji!).ConfigureAwait(false);
                var expectedChannel = message.Channel;
                var waitTextResponseTask = interactivity.WaitForMessageAsync(m => m.Author == user && m.Channel == expectedChannel && !string.IsNullOrEmpty(m.Content), timeout);
                var waitReactionResponse = interactivity.WaitForReactionAsync(arg => reactions.Contains(arg.Emoji), message, user, timeout);
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
                    result = null;
                }
                DiscordMessage? text = null;
                MessageReactionAddEventArgs? reaction = null;
                if (waitTextResponseTask.IsCompletedSuccessfully)
                    text = (await waitTextResponseTask).Result;
                if (waitReactionResponse.IsCompletedSuccessfully)
                    reaction = (await waitReactionResponse).Result;
                if (text != null)
                    try
                    {
                        DeletedMessagesMonitor.RemovedByBotCache.Set(text.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                        await text.DeleteAsync().ConfigureAwait(false);
                    }
                    catch {}
                return (result, text, reaction);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get interactive reaction");
                return (result, null, null);
            }
        }

        public static async Task<(DiscordMessage? text, ComponentInteractionCreateEventArgs? reaction)> WaitForMessageOrButtonAsync(
            this InteractivityExtension interactivity,
            DiscordMessage message,
            DiscordUser user,
            TimeSpan? timeout
        )
        {
            try
            {
                if (message.Channel is null)
                    throw new InvalidOperationException("Provided message.Channel was null");
                
                var expectedChannel = message.Channel;
                var waitButtonTask = interactivity.WaitForButtonAsync(message, user, timeout);
                var waitTextResponseTask = interactivity.WaitForMessageAsync(m => m.Author == user && m.Channel == expectedChannel && !string.IsNullOrEmpty(m.Content), timeout);
                await Task.WhenAny(
                    waitTextResponseTask,
                    waitButtonTask
                ).ConfigureAwait(false);
                DiscordMessage? text = null;
                ComponentInteractionCreateEventArgs? reaction = null;
                if (waitTextResponseTask.IsCompletedSuccessfully)
                    text = (await waitTextResponseTask).Result;
                if (waitButtonTask.IsCompletedSuccessfully)
                {
                    reaction = (await waitButtonTask).Result;
                    if (reaction is not null)
                        await reaction.Interaction.CreateResponseAsync(InteractionResponseType.DeferredMessageUpdate).ConfigureAwait(false);
                }
                if (text != null && !message.Channel.IsPrivate)
                    try
                    {
                        DeletedMessagesMonitor.RemovedByBotCache.Set(text.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                        await text.DeleteAsync().ConfigureAwait(false);
                    }
                    catch {}
                return (text, reaction);                
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get interactive reaction");
                return (null, null);
            }
        }
    }
}
