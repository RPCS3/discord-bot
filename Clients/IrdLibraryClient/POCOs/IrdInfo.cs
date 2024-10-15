namespace IrdLibraryClient.POCOs;

public class IrdInfo
{
    public string Title { get; set; } = null!;
    public string? FwVer { get; set; }
    public string? GameVer { get; set; }
    public string? AppVer { get; set; }
    public string Link { get; set; } = null!;
}