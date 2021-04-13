using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CirrusCiClient.POCOs;
using CompatApiClient;
using CompatApiClient.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using StrawberryShake;

namespace CirrusCiClient
{
    public static class CirrusCi
    {
        private static readonly MemoryCache BuildInfoCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
        private static readonly IServiceProvider ServiceProvider;
        private static IClient Client => ServiceProvider.GetRequiredService<IClient>();

        static CirrusCi()
        {
            var collection = new ServiceCollection();
            collection.AddClient(ExecutionStrategy.CacheAndNetwork)
                .ConfigureHttpClient(c => c.BaseAddress = new("https://api.cirrus-ci.com/graphql"));
            ServiceProvider = collection.BuildServiceProvider();
        }

        public static async Task<BuildInfo?> GetPrBuildInfoAsync(string? commit, DateTime? oldestTimestamp, int pr, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(commit))
                return null;

            commit = commit.ToLower();
            var queryResult = await Client.GetPrBuilds.ExecuteAsync("pull/" + pr, oldestTimestamp.ToTimestamp(), cancellationToken);
            queryResult.EnsureNoErrors();
            if (queryResult.Data?.GithubRepository?.Builds?.Edges is {Count: > 0} edgeList)
            {
                var node = edgeList.LastOrDefault(e => e?.Node?.ChangeIdInRepo  == commit)?.Node;
                if (node is null)
                    return null;

                var winTask = node.Tasks?.FirstOrDefault(t => t?.Name.Contains("Windows") ?? false);
                var winArtifact = winTask?.Artifacts?
                    .Where(a => a?.Files is {Count: >0})
                    .SelectMany(a => a!.Files!)
                    .FirstOrDefault(f => f?.Path.EndsWith(".7z") ?? false);
                var linTask = node.Tasks?.FirstOrDefault(t => t is {} lt && lt.Name.Contains("Linux") && lt.Name.Contains("GCC"));
                var linArtifact = linTask?.Artifacts?
                    .Where(a => a?.Files is {Count: >0})
                    .SelectMany(a => a!.Files!)
                    .FirstOrDefault(a => a?.Path.EndsWith(".AppImage") ?? false);

                var startTime = FromTimestamp(node.BuildCreatedTimestamp);
                var finishTime = GetFinishTime(node);
                return new()
                {
                    Commit = node.ChangeIdInRepo,
                    WindowsFilename = winArtifact?.Path is string wp ? Path.GetFileName(wp) : null,
                    LinuxFilename = linArtifact?.Path is string lp ? Path.GetFileName(lp) : null,
                    WindowsBuildDownloadLink = winTask?.Id is string wtid && winArtifact?.Path is string wtap ? $"https://api.cirrus-ci.com/v1/artifact/task/{wtid}/Artifact/{wtap}" : null,
                    LinuxBuildDownloadLink = linTask?.Id is string ltid && linArtifact?.Path is string ltap ? $"https://api.cirrus-ci.com/v1/artifact/task/{ltid}/Artifact/{ltap}" : null,
                    StartTime = startTime,
                    FinishTime = finishTime,
                    Status = node.Status,
                };
            }
            return null;
        }

        public static async Task<ProjectBuildStats> GetPipelineDurationAsync(CancellationToken cancellationToken)
        {
            const string cacheKey = "project-build-stats";
            if (BuildInfoCache.TryGetValue(cacheKey, out ProjectBuildStats result))
                return result;

            try
            {
                var queryResult = await Client.GetLastFewBuilds.ExecuteAsync(200, cancellationToken).ConfigureAwait(false);
                queryResult.EnsureNoErrors();

                var times = (
                    from edge in queryResult.Data?.GithubRepository?.Builds?.Edges
                    let node = edge?.Node
                    where node?.Status == BuildStatus.Completed
                    let p = new {start = FromTimestamp(node.BuildCreatedTimestamp), finish = GetFinishTime(node)}
                    where p.finish.HasValue
                    let ts = p.finish!.Value - p.start
                    orderby ts descending
                    select ts
                ).ToList();
                if (times.Count <= 10)
                    return ProjectBuildStats.Defaults;
            
                result = new()
                {
                    Percentile95 = times[(int)(times.Count * 0.05)],
                    Percentile90 = times[(int)(times.Count * 0.10)],
                    Percentile85 = times[(int)(times.Count * 0.16)],
                    Percentile80 = times[(int)(times.Count * 0.20)],
                    Mean = TimeSpan.FromTicks(times.Select(t => t.Ticks).Mean()),
                    StdDev = TimeSpan.FromTicks((long)times.Select(t => t.Ticks).StdDev()),
                    BuildCount = times.Count,
                };
                BuildInfoCache.Set(cacheKey, result, TimeSpan.FromDays(1));
                return result;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e, "Failed to get Cirrus build stats");
            }
            return ProjectBuildStats.Defaults;
        }

        private static DateTime? GetFinishTime(IBaseNodeInfo node)
            => node.LatestGroupTasks?
                .Select(t => t?.FinalStatusTimestamp)
                .Where(ts => ts > 0)
                .ToList() is {Count: >0} finalTimes
                ? FromTimestamp(finalTimes.Max()!.Value)
                : node.ClockDurationInSeconds > 0
                    ? FromTimestamp(node.BuildCreatedTimestamp).AddSeconds(node.ClockDurationInSeconds.Value)
                    : (DateTime?)null;

        [return: NotNullIfNotNull(nameof(DateTime))]
        private static string? ToTimestamp(this DateTime? dateTime) => dateTime.HasValue ? (dateTime.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds.ToString("0") : null;
        private static DateTime FromTimestamp(long timestamp) => DateTime.UnixEpoch.AddMilliseconds(timestamp);
    }
}