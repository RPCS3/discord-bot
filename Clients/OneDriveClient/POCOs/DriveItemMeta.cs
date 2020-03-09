using Newtonsoft.Json;

namespace OneDriveClient.POCOs
{
    public class DriveItemMeta
    {
        public string Id;
        public string Name;
        public int Size;
        [JsonProperty(PropertyName = "@odata.context")]
        public string OdataContext;
        [JsonProperty(PropertyName = "@content.downloadUrl")]
        public string ContentDownloadUrl;
    }
}
