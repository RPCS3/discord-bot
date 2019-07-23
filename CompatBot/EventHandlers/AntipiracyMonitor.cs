using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class AntipiracyMonitor
    {
        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            args.Handled = !await ContentFilter.IsClean(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task OnMessageUpdated(MessageUpdateEventArgs args)
        {
            args.Handled = !await ContentFilter.IsClean(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task OnReaction(MessageReactionAddEventArgs e)
        {
            if (e.User.IsBotSafeCheck())
                return;

            var emoji = e.Client.GetEmoji(":piratethink:", Config.Reactions.PiracyCheck);
            if (e.Emoji != emoji)
                return;

            var message = await e.Channel.GetMessageAsync(e.Message.Id).ConfigureAwait(false);
            await ContentFilter.IsClean(e.Client, message).ConfigureAwait(false);
        }
    }
}
