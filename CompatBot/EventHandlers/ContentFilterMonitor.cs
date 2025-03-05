using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;

namespace CompatBot.EventHandlers;

internal static class ContentFilterMonitor
{
    public static Task<bool> OnMessageCreated(DiscordClient c, MessageCreateEventArgs args) => ContentFilter.IsClean(c, args.Message);
    public static Task<bool> OnMessageUpdated(DiscordClient c, MessageUpdateEventArgs args) => ContentFilter.IsClean(c, args.Message);

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