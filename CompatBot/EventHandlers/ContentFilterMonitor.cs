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
        {
            Config.Log.Debug($"[{nameof(ContentFilterMonitor)}] Wrong emoji, skipping");
            return;
        }

        Config.Log.Debug($"[{nameof(ContentFilterMonitor)}] Message has content: {(e.Message is { Content.Length: >0 })}");
        await ContentFilter.IsClean(c, e.Message).ConfigureAwait(false);
    }
}