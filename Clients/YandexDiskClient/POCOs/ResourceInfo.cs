namespace YandexDiskClient.POCOs;
#nullable disable
    
public sealed class ResourceInfo
{
    public int? Size;
    public string Name; //RPCS3.log.gz
    public string PublicKey;
    public string Type; //file
    public string MimeType; //application/x-gzip
    public string File; //<direct download url>
    public string MediaType; //compressed
    public string Md5;
    public string Sha256;
    public long? Revision;
}
    
#nullable restore