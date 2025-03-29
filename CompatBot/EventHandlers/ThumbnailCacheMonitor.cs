using CompatBot.Database;

namespace CompatBot.EventHandlers;

internal static class ThumbnailCacheMonitor
{
    public static async Task OnMessageDeleted(DiscordClient _, MessageDeletedEventArgs args)
    {
        if (args.Channel.Id != Config.ThumbnailSpamId)
            return;

        if (string.IsNullOrEmpty(args.Message.Content))
            return;

        if (!args.Message.Attachments.Any())
            return;

        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        var thumb = wdb.Thumbnail.FirstOrDefault(i => i.ContentId == args.Message.Content);
        if (thumb is { EmbeddableUrl: { Length: > 0 } url } && args.Message.Attachments.Any(a => a.Url == url))
        {
            thumb.EmbeddableUrl = null;
            await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }
    }
}