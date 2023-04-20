using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.ThumbScrapper;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Database.Providers;

internal static class ThumbnailProvider
{
    private static readonly HttpClient HttpClient = HttpClientFactory.Create();
    private static readonly PsnClient.Client PsnClient = new();
    private static readonly MemoryCache ColorCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromDays(1) });

    public static async Task<string?> GetThumbnailUrlAsync(this DiscordClient client, string? productCode)
    {
        if (string.IsNullOrEmpty(productCode))
            return null;
            
        productCode = productCode.ToUpperInvariant();
        var tmdbInfo = await PsnClient.GetTitleMetaAsync(productCode, Config.Cts.Token).ConfigureAwait(false);
        if (tmdbInfo is { Icon.Url: string tmdbIconUrl })
            return tmdbIconUrl;

        await using var db = new ThumbnailDb();
        var thumb = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productCode).ConfigureAwait(false);
        //todo: add search task if not found
        if (thumb?.EmbeddableUrl is {Length: >0} embeddableUrl)
            return embeddableUrl;

        if (string.IsNullOrEmpty(thumb?.Url) || !ScrapeStateProvider.IsFresh(thumb.Timestamp))
        {
            var gameTdbCoverUrl = await GameTdbScraper.GetThumbAsync(productCode).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(gameTdbCoverUrl))
            {
                if (thumb is null)
                    thumb = (await db.Thumbnail.AddAsync(new() {ProductCode = productCode, Url = gameTdbCoverUrl}).ConfigureAwait(false)).Entity;
                else
                    thumb.Url = gameTdbCoverUrl;
                thumb.Timestamp = DateTime.UtcNow.Ticks;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        if (string.IsNullOrEmpty(thumb?.Url))
            return null;
            
        var contentName = thumb.ContentId ?? thumb.ProductCode;
        var (embedUrl, _) = await GetEmbeddableUrlAsync(client, contentName, thumb.Url).ConfigureAwait(false);
        if (embedUrl is null)
            return null;
            
        thumb.EmbeddableUrl = embedUrl;
        await db.SaveChangesAsync().ConfigureAwait(false);
        return embedUrl;
    }

    public static async Task<string?> GetTitleNameAsync(string? productCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(productCode))
            return null;

        productCode = productCode.ToUpperInvariant();
        await using var db = new ThumbnailDb();
        var thumb = await db.Thumbnail.FirstOrDefaultAsync(
            t => t.ProductCode == productCode,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
        if (thumb?.Name is string result)
            return result;

        var title = (await PsnClient.GetTitleMetaAsync(productCode, cancellationToken).ConfigureAwait(false))?.Name;
        try
        {
            if (!string.IsNullOrEmpty(title))
            {
                if (thumb == null)
                    await db.Thumbnail.AddAsync(new()
                    {
                        ProductCode = productCode,
                        Name = title,
                    }, cancellationToken).ConfigureAwait(false);
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

    public static async Task<(string? url, DiscordColor color)> GetThumbnailUrlWithColorAsync(DiscordClient client, string contentId, DiscordColor defaultColor, string? url = null)
    {
        if (string.IsNullOrEmpty(contentId))
            throw new ArgumentException("ContentID can't be empty", nameof(contentId));

        contentId = contentId.ToUpperInvariant();
        await using var db = new ThumbnailDb();
        var info = await db.Thumbnail.FirstOrDefaultAsync(ti => ti.ContentId == contentId, Config.Cts.Token).ConfigureAwait(false);
        info ??= new() {Url = url};
        if (info.Url is null)
            return (null, defaultColor);

        DiscordColor? analyzedColor = null;
        if (string.IsNullOrEmpty(info.EmbeddableUrl))
        {
            var (embedUrl, image) = await GetEmbeddableUrlAsync(client, contentId, info.Url).ConfigureAwait(false);
            if (embedUrl is string eUrl)
            {
                info.EmbeddableUrl = eUrl;
                if (image is byte[] jpg)
                {
                    Config.Log.Trace("Getting dominant color for " + eUrl);
                    analyzedColor = ColorGetter.Analyze(jpg, defaultColor);
                    if (analyzedColor.HasValue
                        && analyzedColor.Value.Value != defaultColor.Value)
                        info.EmbedColor = analyzedColor.Value.Value;
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
        }
        if (!info.EmbedColor.HasValue && !analyzedColor.HasValue
            || info.EmbedColor.HasValue && info.EmbedColor.Value == defaultColor.Value)
        {
            var c = await GetImageColorAsync(info.EmbeddableUrl, defaultColor).ConfigureAwait(false);
            if (c.HasValue && c.Value.Value != defaultColor.Value)
            {
                info.EmbedColor = c.Value.Value;
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
        }
        var color = info.EmbedColor.HasValue ? new(info.EmbedColor.Value) : defaultColor;
        return (info.EmbeddableUrl, color);
    }

    public static async Task<(string? url, byte[]? image)> GetEmbeddableUrlAsync(DiscordClient client, string contentId, string url)
    {
        try
        {
            if (!string.IsNullOrEmpty(Path.GetExtension(url)))
                return (url, null);

            await using var imgStream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false);
            await using var memStream = Config.MemoryStreamManager.GetStream();
            await imgStream.CopyToAsync(memStream).ConfigureAwait(false);
            // minimum jpg size is 119 bytes, png is 67 bytes
            if (memStream.Length < 64)
                return (null, null);

            memStream.Seek(0, SeekOrigin.Begin);
            var spam = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
            var message = await spam.SendMessageAsync(new DiscordMessageBuilder().AddFile(contentId + ".jpg", memStream).WithContent(contentId)).ConfigureAwait(false);
            url = message.Attachments[0].Url;
            return (url, memStream.ToArray());
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
        return (null, null);
    }

    public static async Task<DiscordColor?> GetImageColorAsync(string? url, DiscordColor? defaultColor = null)
    {
        try
        {
            if (string.IsNullOrEmpty(url))
                return null;

            if (ColorCache.TryGetValue(url, out DiscordColor? result))
                return result;

            await using var imgStream = await HttpClient.GetStreamAsync(url).ConfigureAwait(false);
            await using var memStream = Config.MemoryStreamManager.GetStream();
            await imgStream.CopyToAsync(memStream).ConfigureAwait(false);
            // minimum jpg size is 119 bytes, png is 67 bytes
            if (memStream.Length < 64)
                return null;

            memStream.Seek(0, SeekOrigin.Begin);

            Config.Log.Trace("Getting dominant color for " + url);
            result = ColorGetter.Analyze(memStream.ToArray(), defaultColor);
            ColorCache.Set(url, result, TimeSpan.FromHours(1));
            return result;
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
        return null;
    }
}