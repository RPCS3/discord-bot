using System;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
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

        public static async Task OnReaction(MessageReactionAddEventArgs e)
        {
            if (e.User.IsBotSafeCheck())
                return;

            var emoji = e.Client.GetEmoji(":piratethink:", Config.Reactions.PiracyCheck);
            if (e.Emoji != emoji)
                return;

            var message = await e.Channel.GetMessageAsync(e.Message.Id).ConfigureAwait(false);
            await IsClean(e.Client, message).ConfigureAwait(false);
        }

        public static async Task<bool> IsClean(DiscordClient client, DiscordMessage message)
        {
            if (message.Channel.IsPrivate)
                return true;

            if (message.Author.IsBotSafeCheck())
                return true;

#if !DEBUG
            if (message.Author.IsWhitelisted(client, message.Channel.Guild))
                return true;
#endif

            if (string.IsNullOrEmpty(message.Content))
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
                var yarr = client.GetEmoji(":piratethink:", "🦜");
                await Task.WhenAll(
                    message.Channel.SendMessageAsync($"{message.Author.Mention} Please follow the {rules.Mention} and do not discuss piracy on this server. Repeated offence may result in a ban."),
                    client.ReportAsync(yarr + " Mention of piracy", message, trigger, message.Content, severity),
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
