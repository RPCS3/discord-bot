namespace CompatApiClient.POCOs;

public class UpdateCheckResult
{
    public StatusCode ReturnCode;
    public BuildInfo LatestBuild = null!;
    public BuildInfo? CurrentBuild;
    public VersionInfo[]? Changelog;
}

public class BuildInfo
{
    public int? Pr;
    public string Datetime = null!;
    public string Version = null!;
    public BuildLink? Windows;
    public BuildLink? Linux;
    public BuildLink? Mac;
}

public class BuildLink
{
    public string Download = null!;
    public int? Size;
    public string? Checksum;
}

public class VersionInfo
{
    public string Verison = null!;
    public string? Title;
}

public enum StatusCode
{
    IllegalSearch = -3,
    Maintenance = -2,
    UnknownBuild = -1,
    NoUpdates = 0,
    UpdatesAvailable = 1,
}

public static class ArchType
{
    public const string X64 = "x64";
    public const string Arm = "arm64";
}
