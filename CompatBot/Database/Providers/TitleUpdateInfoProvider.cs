using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using CompatBot.Utils;
using Microsoft.EntityFrameworkCore;
using PsnClient.POCOs;

namespace CompatBot.Database.Providers
{
    public static class TitleUpdateInfoProvider
    {
        private static readonly PsnClient.Client Client = new();

        public static async Task<TitlePatch?> GetAsync(string? productId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(productId))
                return default;

            productId = productId.ToUpper();
            var (update, xml) = await Client.GetTitleUpdatesAsync(productId, cancellationToken).ConfigureAwait(false);
            if (xml is string {Length: > 10})
            {
                var xmlChecksum = xml.GetStableHash();
                await using var db = new ThumbnailDb();
                var updateInfo = db.GameUpdateInfo.FirstOrDefault(ui => ui.ProductCode == productId);
                if (updateInfo is null)
                    db.GameUpdateInfo.Add(new() {ProductCode = productId, MetaHash = xmlChecksum, MetaXml = xml, Timestamp = DateTime.UtcNow.Ticks});
                else if (updateInfo.MetaHash != xmlChecksum && update?.Tag?.Packages is {Length: >0})
                {
                    updateInfo.MetaHash = xmlChecksum;
                    updateInfo.MetaXml = xml;
                    updateInfo.Timestamp = DateTime.UtcNow.Ticks;
                }
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            if ((update?.Tag?.Packages?.Length ?? 0) == 0)
            {
                await using var db = new ThumbnailDb();
                var updateInfo = db.GameUpdateInfo.FirstOrDefault(ui => ui.ProductCode == productId);
                if (updateInfo is null)
                    return update;

                await using var memStream = Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(updateInfo.MetaXml));
                var xmlSerializer = new XmlSerializer(typeof(TitlePatch));
                update = (TitlePatch?)xmlSerializer.Deserialize(memStream);
            }
            
            return update;
        }

        public static async Task RefreshGameUpdateInfoAsync(CancellationToken cancellationToken)
        {
            await using var db = new ThumbnailDb();
            do
            {
                var productCodeList = await db.Thumbnail.AsNoTracking().Select(t => t.ProductCode).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var titleId in productCodeList)
                {
                    var updateInfo = db.GameUpdateInfo.AsNoTracking().FirstOrDefault(ui => ui.ProductCode == titleId);
                    if (!cancellationToken.IsCancellationRequested
                        && ((updateInfo?.Timestamp ?? 0) == 0 || updateInfo!.Timestamp.AsUtc() < DateTime.UtcNow.AddMonths(-1)))
                    {
                        await GetAsync(titleId, cancellationToken).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    }
                }
                await Task.Delay(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
            } while (!cancellationToken.IsCancellationRequested);
        }
    }
}