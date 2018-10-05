using System;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database.Providers;
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

        public static async Task OnMessageUpdated(MessageUpdateEventArgs args)
        {
            args.Handled = !await IsClean(args.Client, args.Message).ConfigureAwait(false);
        }

        private static async Task<bool> IsClean(DiscordClient client, DiscordMessage message)
        {
            if (DefaultHandlerFilter.IsFluff(message))
                return true;

            if (message.Author.IsWhitelisted(client, message.Channel.Guild))
                return true;

            string trigger = null;
            var severity = ReportSeverity.Low;
            try
            {
                trigger = await PiracyStringProvider.FindTriggerAsync(message.Content);
                if (trigger == null)
                    return true;

                await message.Channel.DeleteMessageAsync(message, $"Mention of piracy trigger '{trigger}'").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't delete message in {message.Channel.Name}");
                severity = ReportSeverity.High;
            }
            try
            {
                var rules = await client.GetChannelAsync(Config.BotRulesChannelId).ConfigureAwait(false);
                await Task.WhenAll(
                    message.Channel.SendMessageAsync($"{message.Author.Mention} Please follow the {rules.Mention} and do not discuss piracy on this server. Repeated offence may result in a ban."),
                    client.ReportAsync("Mention of piracy", message, trigger, message.Content, severity),
                    Warnings.AddAsync(client, message, message.Author.Id, message.Author.Username, client.CurrentUser, "Mention of piracy", message.Content.Sanitize())
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't finish piracy trigger actions for a message in {message.Channel.Name}");
            }
            return false;
        }
    }
}
