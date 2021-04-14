using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using CompatBot.Utils;
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
                    db.GameUpdateInfo.Add(new() {ProductCode = productId, MetaHash = xmlChecksum, MetaXml = xml});
                else if (updateInfo.MetaHash != xmlChecksum && update?.Tag?.Packages is {Length: >0})
                {
                    updateInfo.MetaHash = xmlChecksum;
                    updateInfo.MetaXml = xml;
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
    }
}