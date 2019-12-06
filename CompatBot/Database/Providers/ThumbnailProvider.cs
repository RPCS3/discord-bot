using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.ThumbScrapper;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers
{
    internal static class ThumbnailProvider
    {
        private static readonly HttpClient HttpClient = HttpClientFactory.Create();
        private static readonly PsnClient.Client PsnClient = new PsnClient.Client();

        public static async Task<string> GetThumbnailUrlAsync(this DiscordClient client, string productCode)
        {
            productCode = productCode.ToUpperInvariant();
            var tmdbInfo = await PsnClient.GetTitleMetaAsync(productCode, Config.Cts.Token).ConfigureAwait(false);
            if (tmdbInfo?.Icon.Url is string tmdbIconUrl)
                return tmdbIconUrl;

            using (var db = new ThumbnailDb())
            {
                var thumb = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productCode).ConfigureAwait(false);
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
                    var contentName = (thumb.ContentId ?? thumb.ProductCode);
                    var embed = await GetEmbeddableUrlAsync(client, contentName, url).ConfigureAwait(false);

                    if (embed.url != null)
                    {
                        thumb.EmbeddableUrl = embed.url;
                        await db.SaveChangesAsync().ConfigureAwait(false);
                        return embed.url;
                    }
                }
            }
            return null;
        }

        public static async Task<string> GetTitleNameAsync(string productCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(productCode))
                return null;

            productCode = productCode.ToUpperInvariant();
            using var db = new ThumbnailDb();
            var thumb = await db.Thumbnail.FirstOrDefaultAsync(
                t => t.ProductCode == productCode,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            if (thumb?.Name is string title)
                return title;

            var meta = await PsnClient.GetTitleMetaAsync(productCode, cancellationToken).ConfigureAwait(false);
            title = meta?.Name;
            try
            {
                if (!string.IsNullOrEmpty(title))
                {
                    if (thumb == null)
                        thumb = (
                            await db.Thumbnail.AddAsync(new Thumbnail
                            {
                                ProductCode = productCode,
                                Name = title,
                            }, cancellationToken).ConfigureAwait(false)
                        ).Entity;
                    else
                        thumb.Name = title;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }

            return title;
        }

        public static async Task<(string url, DiscordColor color)> GetThumbnailUrlWithColorAsync(DiscordClient client, string contentId, DiscordColor defaultColor, string url = null)
        {
            if (string.IsNullOrEmpty(contentId))
                throw new ArgumentException("ContentID can't be empty", nameof(contentId));

            contentId = contentId.ToUpperInvariant();
            using var db = new ThumbnailDb();
            var info = await db.TitleInfo.FirstOrDefaultAsync(ti => ti.ContentId == contentId, Config.Cts.Token).ConfigureAwait(false);
            if (info == null)
            {
                info = new TitleInfo {ContentId = contentId, ThumbnailUrl = url, Timestamp = DateTime.UtcNow.Ticks};
                var thumb = await db.Thumbnail.FirstOrDefaultAsync(t => t.ContentId == contentId).ConfigureAwait(false);
                if (thumb?.EmbeddableUrl is string eUrl
                    && thumb.Url is string thumbUrl
                    && thumbUrl == url)
                    info.ThumbnailEmbeddableUrl = eUrl;
                info = db.TitleInfo.Add(info).Entity;
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            DiscordColor? analyzedColor = null;
            if (string.IsNullOrEmpty(info.ThumbnailEmbeddableUrl))
            {
                var em = await GetEmbeddableUrlAsync(client, contentId, info.ThumbnailUrl).ConfigureAwait(false);
                if (em.url is string eUrl)
                {
                    info.ThumbnailEmbeddableUrl = eUrl;
                    if (em.image is byte[] jpg)
                    {
                        analyzedColor = ColorGetter.Analyze(jpg, defaultColor);
                        var c = analyzedColor.Value.Value;
                        if (c != defaultColor.Value)
                            info.EmbedColor = c;
                    }
                    await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                }
            }
            if ((!info.EmbedColor.HasValue && !analyzedColor.HasValue)
                || (info.EmbedColor.HasValue && info.EmbedColor.Value == defaultColor.Value))
            {
                var c = await GetImageColorAsync(info.ThumbnailEmbeddableUrl, defaultColor).ConfigureAwait(false);
                if (c.HasValue && c.Value.Value != defaultColor.Value)
                {
                    info.EmbedColor = c.Value.Value;
                    await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                }
            }
            var color = info.EmbedColor.HasValue ? new DiscordColor(info.EmbedColor.Value) : defaultColor;
            return (info.ThumbnailEmbeddableUrl, color);
        }

        public static async Task<(string url, byte[] image)> GetEmbeddableUrlAsync(DiscordClient client, string contentId, string url)
        {
            try
            {
                if (!string.IsNullOrEmpty(Path.GetExtension(url)))
                    return (url, null);

                using var imgStream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false);
                using var memStream = Config.MemoryStreamManager.GetStream();
                await imgStream.CopyToAsync(memStream).ConfigureAwait(false);
                // minimum jpg size is 119 bytes, png is 67 bytes
                if (memStream.Length < 64)
                    return (null, null);

                memStream.Seek(0, SeekOrigin.Begin);
                var spam = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                var message = await spam.SendFileAsync(contentId + ".jpg", memStream, contentId).ConfigureAwait(false);
                url = message.Attachments.First().Url;
                return (url, memStream.ToArray());
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
            return (null, null);
        }

        private static async Task<DiscordColor?> GetImageColorAsync(string url, DiscordColor defaultColor)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                    return null;

                using var imgStream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false);
                using var memStream = Config.MemoryStreamManager.GetStream();
                await imgStream.CopyToAsync(memStream).ConfigureAwait(false);
                // minimum jpg size is 119 bytes, png is 67 bytes
                if (memStream.Length < 64)
                    return null;

                memStream.Seek(0, SeekOrigin.Begin);

                return ColorGetter.Analyze(memStream.ToArray(), defaultColor);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
            return null;
        }
    }
}
