using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace IrdLibraryClient.POCOs;

public sealed class SearchResult
{
    public List<SearchResultItem>? Data;
}

public sealed class SearchResultItem
{
    public string? Id; // product code
    public string? Title;
    public string? GameVersion;
    public string? UpdateVersion;
    public string? Size;
    public string? FileCount;
    public string? FolderCount;
    public string? MD5;
    public string? IrdName;

    public string? Filename;
}