using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CompatBot.ThumbScrapper;
using DSharpPlus;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers
{
    internal static class ThumbnailProvider
    {
        private static readonly HttpClient HttpClient = HttpClientFactory.Create();

        public static async Task<string> GetThumbnailUrlAsync(this DiscordClient client, string productCode)
        {
            productCode = productCode.ToUpperInvariant();
            using (var db = new ThumbnailDb())
            {
                var thumb = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productCode.ToUpperInvariant()).ConfigureAwait(false);
                //todo: add search task if not found
                if (thumb?.EmbeddableUrl is string embeddableUrl && !string.IsNullOrEmpty(embeddableUrl))
                    return embeddableUrl;

                if (string.IsNullOrEmpty(thumb?.Url) || !ScrapeStateProvider.IsFresh(thumb.Timestamp))
                {
                    var gameTdbCoverUrl = await GameTdbScraper.GetThumbAsync(productCode).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(gameTdbCoverUrl))
                    {
                        if (thumb == null)
                        {
                            var addResult = await db.Thumbnail.AddAsync(new Thumbnail {ProductCode = productCode, Url = gameTdbCoverUrl}).ConfigureAwait(false);
                            thumb = addResult.Entity;
                        }
                        else
                            thumb.Url = gameTdbCoverUrl;
                        thumb.Timestamp = DateTime.UtcNow.Ticks;
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                if (thumb?.Url is string url && !string.IsNullOrEmpty(url))
                {
                    if (!string.IsNullOrEmpty(Path.GetExtension(url)))
                    {
                        thumb.EmbeddableUrl = url;
                        await db.SaveChangesAsync().ConfigureAwait(false);
                        return url;
                    }

                    try
                    {
                        using (var imgStream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false))
                        using (var memStream = new MemoryStream())
                        {
                            await imgStream.CopyToAsync(memStream).ConfigureAwait(false);
                            // minimum jpg size is 119 bytes, png is 67 bytes
                            if (memStream.Length < 64)
                                return null;
                            memStream.Seek(0, SeekOrigin.Begin);
                            var spam = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                            //var message = await spam.SendFileAsync(memStream, (thumb.ContentId ?? thumb.ProductCode) + ".jpg").ConfigureAwait(false);
                            var contentName = (thumb.ContentId ?? thumb.ProductCode);
                            var message = await spam.SendFileAsync(contentName + ".jpg", memStream, contentName).ConfigureAwait(false);
                            thumb.EmbeddableUrl = message.Attachments.First().Url;
                            await db.SaveChangesAsync().ConfigureAwait(false);
                            return thumb.EmbeddableUrl;
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e);
                    }
                }
            }
            return null;
        }

        public static string GetTitleName(string productCode)
        {
            if (string.IsNullOrEmpty(productCode))
                return null;

            using (var db = new ThumbnailDb())
            {
                var thumb = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productCode.ToUpperInvariant());
                return thumb?.Name;
            }
        }
    }
}
