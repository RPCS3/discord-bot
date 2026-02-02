using CompatBot.Utils.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.Services.DelegatedAuthorization;

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
        if (msg is { Components: [{ Type: (DiscordComponentType)20 }] }
            && !args.Channel.IsOfftopicChannel())
        {
            await msg.DeleteAsync().ConfigureAwait(false);
            Config.Log.Debug($"Removed checkpoint spam message from user {author.Username} ({author.Id}) in #{msg.Channel?.Name}");
            return false;
        }


        if (MessageCache.TryGetValue(author.Id, out var item)
            && item is (DiscordMessage lastMessage, bool isWarned)
            && SameContent(lastMessage, msg)
            && lastMessage.ChannelId != msg.ChannelId)
        {
            var removedSpam = false;
            try
            {
                await msg.DeleteAsync("spam").ConfigureAwait(false);
                removedSpam = true;

                var newMsgContent = "<???>";
                if (msg.Content is { Length: > 0 })
                {
                    newMsgContent = msg.Content;
                }
                else if (msg is { Attachments.Count: > 0 })
                {
                    foreach (var att in msg.Attachments)
                        newMsgContent += $"📎 {att.FileName}\n";
                    newMsgContent = newMsgContent.TrimEnd();
                }
                Config.Log.Debug($"""
                    Removed spam message from user {author.Username} ({author.Id}) in #{msg.Channel?.Name}:
                    {newMsgContent.Trim()}
                    """
                );
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to delete spam message from user {author.Username} ({author.Id}) in #{msg.Channel?.Name} {msg.JumpLink}");
            }
            try
            {
                if (!isWarned)
                {
                    await author.SendMessageAsync("Please do not spam the same message in multiple channels. Thank you.").ConfigureAwait(false);
                    try
                    {
                        if (await client.GetMemberAsync(author).ConfigureAwait(false) is DiscordMember member)
                            await member.TimeoutAsync(DateTimeOffset.UtcNow.AddMinutes(1), "Anti-spam filter").ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to timeout user {author.Username}");
                    }
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

    private static bool SameContent(DiscordMessage msg1, DiscordMessage msg2)
    {
        if (msg1 is { Content.Length: > 0 }
            && msg2 is { Content.Length: > 0 }
            && msg1.Content == msg2.Content)
            return true;

        if (msg1 is { Attachments.Count: > 0 }
            && msg2 is { Attachments.Count: > 0 }
            && msg1.Attachments.OrderByDescending(a => a.FileSize)
                .SequenceEqual(msg2.Attachments.OrderByDescending(a => a.FileSize), DiscordAttachmentFuzzyComparer.Instance))
            return true;

        return false;
    }
}