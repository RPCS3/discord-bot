using System.Text.Json.Serialization;

namespace OneDriveClient.POCOs;

public sealed class DriveItemMeta
{
    public string? Id;
    public string? Name;
    public int Size;
    [JsonPropertyName("@odata.context")]
    public string? OdataContext;
    [JsonPropertyName("@content.downloadUrl")]
    public string? ContentDownloadUrl;
}