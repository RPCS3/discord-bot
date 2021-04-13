using System;

namespace CirrusCiClient.POCOs
{
    public record ProjectBuildStats
    {
        public TimeSpan Percentile95 { get; init; }
        public TimeSpan Percentile90 { get; init; }
        public TimeSpan Percentile85 { get; init; }
        public TimeSpan Percentile80 { get; init; }
        public TimeSpan Mean { get; init; }
        public TimeSpan StdDev { get; init; }
        public int BuildCount { get; init; }

        public static readonly ProjectBuildStats Defaults = new()
        {
            Percentile95 = TimeSpan.FromSeconds(1120),
            Percentile90 = TimeSpan.FromSeconds(900),
            Percentile85 = TimeSpan.FromSeconds(870),
            Percentile80 = TimeSpan.FromSeconds(865),
            Mean = TimeSpan.FromSeconds(860),
            StdDev = TimeSpan.FromSeconds(420),
        };
    }
}