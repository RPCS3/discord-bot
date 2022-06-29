using System;
using CirrusCiClient.Generated;

namespace CirrusCiClient.POCOs;

public record BuildOSInfo
{
    public string? Filename { get; init; }
    public string? DownloadLink { get; init; }
    public TaskStatus? Status { get; init; }
}
public record BuildInfo
{
    public string? Commit { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? FinishTime { get; init; }
    public BuildOSInfo? WindowsBuild { get; init; }
    public BuildOSInfo? LinuxBuild { get; init; }
    public BuildOSInfo? MacBuild { get; init; }
}