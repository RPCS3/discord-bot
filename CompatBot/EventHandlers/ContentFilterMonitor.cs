using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;

namespace CompatBot.EventHandlers;

internal static class ContentFilterMonitor
{
    public static async Task<bool> OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args) => await ContentFilter.IsClean(c, args.Message).ConfigureAwait(false);
    public static async Task<bool> OnMessageUpdated(DiscordClient c, MessageUpdatedEventArgs args) => await ContentFilter.IsClean(c, args.Message).ConfigureAwait(false);

    public static async Task OnReaction(DiscordClient c, MessageReactionAddedEventArgs e)
    {
        if (e.User.IsBotSafeCheck())
            return;

        var emoji = c.GetEmoji(":piratethink:", Config.Reactions.PiracyCheck);
        Config.Log.Debug($"[{nameof(ContentFilterMonitor)}] Resolved emoji: {emoji}, reaction emoji: {e.Emoji}");
        if (e.Emoji != emoji)
            return;

        var message = await e.Channel.GetMessageCachedAsync(e.Message.Id).ConfigureAwait(false)
                      ?? await e.Channel.GetMessageAsync(e.Message.Id).ConfigureAwait(false);
        await ContentFilter.IsClean(c, message).ConfigureAwait(false);
    }
}