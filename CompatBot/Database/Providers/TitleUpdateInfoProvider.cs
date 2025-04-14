using System.Xml.Serialization;
using Microsoft.EntityFrameworkCore;
using PsnClient.POCOs;

namespace CompatBot.Database.Providers;

public static class TitleUpdateInfoProvider
{
    private static readonly PsnClient.Client Client = new();
    private static readonly XmlSerializer XmlSerializer = new(typeof(TitlePatch));

    public static async ValueTask<TitlePatch?> GetAsync(string? productId, CancellationToken cancellationToken)
        => await GetCachedAsync(productId, false).ConfigureAwait(false)
           ?? await GetFromApiAsync(productId, cancellationToken).ConfigureAwait(false)
           ?? await GetCachedAsync(productId, true).ConfigureAwait(false);

    private static async ValueTask<TitlePatch?> GetFromApiAsync(string? productId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(productId))
            return null;

        productId = productId.ToUpper();
        try
        {
            using var cts = new CancellationTokenSource(5_000);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var (update, xml) = await Client.GetTitleUpdatesAsync(productId, linkedCts.Token).ConfigureAwait(false);
            if (xml is { Length: > 10 })
            {
                var xmlChecksum = xml.GetStableHash();
                await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
                var updateInfo = wdb.GameUpdateInfo.FirstOrDefault(ui => ui.ProductCode == productId);
                if (updateInfo is null)
                    wdb.GameUpdateInfo.Add(new()
                    {
                        ProductCode = productId, MetaHash = xmlChecksum, MetaXml = xml,
                        Timestamp = DateTime.UtcNow.Ticks
                    });
                else if (updateInfo.MetaHash != xmlChecksum && update?.Tag?.Packages is { Length: > 0 })
                {
                    updateInfo.MetaHash = xmlChecksum;
                    updateInfo.MetaXml = xml;
                    updateInfo.Timestamp = DateTime.UtcNow.Ticks;
                }
                else if (updateInfo.MetaHash == xmlChecksum)
                    updateInfo.Timestamp = DateTime.UtcNow.Ticks;
                await wdb.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            if (update is { Tag.Packages.Length: > 0 })
                return update;
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to get updates information from PSN API");
        }
        return null;
    }

    private static async ValueTask<TitlePatch?> GetCachedAsync(string? productId, bool returnStale = false)
    {
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        var updateInfo = db.GameUpdateInfo
            .AsNoTracking()
            .FirstOrDefault(ui => ui.ProductCode == productId);
        if (updateInfo is null
            || (!returnStale && updateInfo.Timestamp < DateTime.UtcNow.AddDays(-1).Ticks))
            return null;

        await using var memStream = Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(updateInfo.MetaXml));
        var update = (TitlePatch?)XmlSerializer.Deserialize(memStream);
        if (update is null)
            return null;
        
        update.OfflineCacheTimestamp = updateInfo.Timestamp.AsUtc();
        return update;
    }

    public static async Task RefreshGameUpdateInfoAsync(CancellationToken cancellationToken)
    {
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        do
        {
            var productCodeList = await db.Thumbnail.AsNoTracking().Select(t => t.ProductCode).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var titleId in productCodeList)
            {
                var updateInfo = db.GameUpdateInfo.AsNoTracking().FirstOrDefault(ui => ui.ProductCode == titleId);
                if (!cancellationToken.IsCancellationRequested
                    && (updateInfo?.Timestamp is null or 0L || updateInfo.Timestamp.AsUtc() < DateTime.UtcNow.AddMonths(-1)))
                {
                    await GetAsync(titleId, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                }
            }
            await Task.Delay(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
        } while (!cancellationToken.IsCancellationRequested);
    }
}