using System;

namespace CirrusCiClient.POCOs
{
    public record BuildInfo
    {
        public string? Commit { get; init; }
        public string? WindowsFilename { get; init; }
        public string? LinuxFilename { get; init; }
        public string? WindowsBuildDownloadLink { get; init; }
        public string? LinuxBuildDownloadLink { get; init; }
        public DateTime StartTime { get; init; }
        public DateTime? FinishTime { get; init; }
        public BuildStatus? Status { get; init; }
    }
}