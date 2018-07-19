using System;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Providers;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class AntipiracyMonitor
    {
        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            args.Handled = !await IsClean(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task OnMessageEdit(MessageUpdateEventArgs args)
        {
            args.Handled = !await IsClean(args.Client, args.Message).ConfigureAwait(false);
        }

        private static async Task<bool> IsClean(DiscordClient client, DiscordMessage message)
        {
            if (message.Author.IsBot)
                return true;

            if (string.IsNullOrEmpty(message.Content) || message.Content.StartsWith(Config.CommandPrefix))
                return true;

            string trigger = null;
            bool needsAttention = false;
            try
            {
                trigger = await PiracyStringProvider.FindTriggerAsync(message.Content);
                if (trigger == null)
                    return true;

                await message.Channel.DeleteMessageAsync(message, $"Mention of piracy trigger '{trigger}'").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Warning, "", $"Couldn't delete message in {message.Channel.Name}: {e.Message}", DateTime.Now);
                needsAttention = true;
            }
            try
            {
                var rules = await client.GetChannelAsync(Config.BotRulesChannelId).ConfigureAwait(false);
                await Task.WhenAll(
                    message.Channel.SendMessageAsync($"{message.Author.Mention} Please follow the {rules.Mention} and do not discuss piracy on this server. Repeated offence may result in a ban."),
                    client.ReportAsync("Mention of piracy", message, trigger, message.Content, needsAttention),
                    Warnings.AddAsync(client, message, message.Author.Id, message.Author.Username, client.CurrentUser, "Mention of piracy", message.Content)
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Warning, "", $"Couldn't finish piracy trigger actions for a message in {message.Channel.Name}: {e}", DateTime.Now);
            }
            return false;
        }
    }
}
