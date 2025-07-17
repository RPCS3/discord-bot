using CompatBot.Utils.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

public static class AntiSpamMessageHandler
{
    private static readonly MemoryCache MessageCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(1) });
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromSeconds(10);

    public static async Task<bool> OnMessageCreated(DiscordClient client, MessageCreatedEventArgs args)
    {
        var author = args.Author;
        if (author.IsBotSafeCheck())
            return true;

#if !DEBUG
        if (await author.IsSmartlistedAsync(client, args.Guild).ConfigureAwait(false)
            || await author.IsWhitelistedAsync(client, args.Guild).ConfigureAwait(false))
            return true;
#endif

        var msg = args.Message;
        if (msg.Content is not { Length: > 0 })
            return true;

        if (MessageCache.TryGetValue(author.Id, out var item)
            && item is (DiscordMessage { Content.Length: >0 } lastMessage, bool isWarned)
            && lastMessage.Content == msg.Content
            && lastMessage.ChannelId != msg.ChannelId)
        {
            var removedSpam = false;
            try
            {
                await msg.DeleteAsync("spam").ConfigureAwait(false);
                removedSpam = true;
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Faled to delete spam message from user {author.Username} ({author.Id}) in #{msg.Channel?.Name} {msg.JumpLink}");
            }
            try
            {
                if (!isWarned)
                {
                    await author.SendMessageAsync("Please do not spam the same message in multiple channels. Thank you.").ConfigureAwait(false);
                    isWarned = true;
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Faled to send DM to user {author.Username} ({author.Id})");
            }
            MessageCache.Set(author.Id, (lastMessage, isWarned), DefaultExpiration);
            return !removedSpam; // couldn't remove, need to check with filters etc
        }

        MessageCache.Set(author.Id, (msg, false), DefaultExpiration);
        return true;
    }
}