using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class ThumbnailCacheMonitor
    {
        public static async Task OnMessageDeleted(MessageDeleteEventArgs args)
        {
            if (args.Channel.Id != Config.ThumbnailSpamId)
                return;

            if (string.IsNullOrEmpty(args.Message.Content))
                return;

            if (!args.Message.Attachments.Any())
                return;

            using (var db = new ThumbnailDb())
            {
                var thumb = db.Thumbnail.FirstOrDefault(i => i.ContentId == args.Message.Content);
                if (thumb?.EmbeddableUrl is string url && !string.IsNullOrEmpty(url) && args.Message.Attachments.Any(a => a.Url == url))
                {
                    thumb.EmbeddableUrl = null;
                    await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
