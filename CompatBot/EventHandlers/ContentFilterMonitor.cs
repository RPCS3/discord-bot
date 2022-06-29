using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers;

internal static class ContentFilterMonitor
{
    public static async Task OnMessageCreated(DiscordClient c, MessageCreateEventArgs args)
    {
        args.Handled = !await ContentFilter.IsClean(c, args.Message).ConfigureAwait(false);
    }

    public static async Task OnMessageUpdated(DiscordClient c, MessageUpdateEventArgs args)
    {
        args.Handled = !await ContentFilter.IsClean(c, args.Message).ConfigureAwait(false);
    }

    public static async Task OnReaction(DiscordClient c, MessageReactionAddEventArgs e)
    {
        if (e.User.IsBotSafeCheck())
            return;

        var emoji = c.GetEmoji(":piratethink:", Config.Reactions.PiracyCheck);
        if (e.Emoji != emoji)
            return;

        var message = await e.Channel.GetMessageAsync(e.Message.Id).ConfigureAwait(false);
        await ContentFilter.IsClean(c, message).ConfigureAwait(false);
    }
}