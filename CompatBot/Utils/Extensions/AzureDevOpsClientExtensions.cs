using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.TeamFoundation.Build.WebApi;
using SharpCompress.Readers;

namespace CompatBot.Utils.Extensions
{
    internal static class AzureDevOpsClientExtensions
    {
        private static readonly MemoryCache BuildInfoCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

        public class BuildInfo
        {
            public string? Commit;
            public string? WindowsFilename;
            public string? LinuxFilename;
            public string? WindowsBuildDownloadLink;
            public string? LinuxBuildDownloadLink;
            public DateTime? StartTime;
            public DateTime? FinishTime;
            public BuildStatus? Status;
            public BuildResult? Result { get; set; }
        }

        public class PipelineStats
        {
            public TimeSpan Percentile95;
            public TimeSpan Percentile90;
            public TimeSpan Percentile85;
            public TimeSpan Percentile80;
            public TimeSpan Mean;
            public TimeSpan StdDev;
            public int BuildCount;

            public static readonly PipelineStats Defaults = new PipelineStats
            {
                Percentile95 = TimeSpan.FromMinutes(33.696220415),
                Percentile90 = TimeSpan.FromMinutes(32.635776191666665),
                Percentile85 = TimeSpan.FromMinutes(32.17856230833333),
                Percentile80 = TimeSpan.FromMinutes(31.885896321666667),
                Mean = TimeSpan.FromMinutes(28.875494935),
                StdDev = TimeSpan.FromMinutes(2.8839262116666666),
            };
        }

        public static async Task<PipelineStats> GetPipelineDurationAsync(this BuildHttpClient? azureDevOpsClient, CancellationToken cancellationToken)
        {
            const string cacheKey = "pipeline-duration";
            if (BuildInfoCache.TryGetValue(cacheKey, out PipelineStats result))
                return result;

            if (azureDevOpsClient is null)
                return PipelineStats.Defaults;

            var builds = await azureDevOpsClient.GetBuildsAsync(
                Config.AzureDevOpsProjectId,
                repositoryId: "RPCS3/rpcs3",
                repositoryType: "GitHub",
                statusFilter: BuildStatus.Completed,
                resultFilter: BuildResult.Succeeded,
                minFinishTime: DateTime.UtcNow.AddDays(-7),
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            var times = builds
                .Where(b => b is {StartTime: not null, FinishTime: not null})
                .Select(b => (b.FinishTime - b.StartTime)!.Value)
                .OrderByDescending(t => t)
                .ToList();
            if (times.Count <= 10)
                return PipelineStats.Defaults;
            
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
            BuildInfoCache.Set(cacheKey, result, TimeSpan.FromDays(7));
            return result;
        }
        
        public static async Task<List<BuildInfo>?> GetMasterBuildsAsync(this BuildHttpClient? azureDevOpsClient, string? oldestMergeCommit, string? newestMergeCommit, DateTime? oldestTimestamp, CancellationToken cancellationToken)
        {
            if (azureDevOpsClient == null || string.IsNullOrEmpty(oldestMergeCommit) || string.IsNullOrEmpty(newestMergeCommit))
                return null;

            oldestMergeCommit = oldestMergeCommit.ToLower();
            newestMergeCommit = newestMergeCommit.ToLower();
            var builds = await azureDevOpsClient.GetBuildsAsync(
                Config.AzureDevOpsProjectId,
                repositoryId: "RPCS3/rpcs3",
                repositoryType: "GitHub",
                reasonFilter: BuildReason.IndividualCI,
                minFinishTime: oldestTimestamp,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            builds = builds
                .Where(b => b.SourceBranch == "refs/heads/master" && b.Status == BuildStatus.Completed)
                .OrderByDescending(b => b.StartTime)
                .ToList();
            builds = builds
                .SkipWhile(b => !newestMergeCommit.Equals(b.SourceVersion, StringComparison.InvariantCultureIgnoreCase))
                .Skip(1)
                .TakeWhile(b => !oldestMergeCommit.Equals(b.SourceVersion, StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            await Task.WhenAll(builds.Select(b => azureDevOpsClient.GetArtifactsInfoAsync(b.SourceVersion, b, cancellationToken))).ConfigureAwait(false);
            return builds.Select(b => azureDevOpsClient.GetArtifactsInfoAsync(b.SourceVersion, b, cancellationToken).GetAwaiter().GetResult()).ToList();
        }

        public static async Task<BuildInfo?> GetMasterBuildInfoAsync(this BuildHttpClient? azureDevOpsClient, string? commit, DateTime? oldestTimestamp, CancellationToken cancellationToken)
        {
            if (azureDevOpsClient == null || string.IsNullOrEmpty(commit))
                return null;

            commit = commit.ToLower();
            if (BuildInfoCache.TryGetValue(commit, out BuildInfo result))
                return result;

            var builds = await azureDevOpsClient.GetBuildsAsync(
                Config.AzureDevOpsProjectId,
                repositoryId: "RPCS3/rpcs3",
                repositoryType: "GitHub",
                reasonFilter: BuildReason.IndividualCI,
                minFinishTime: oldestTimestamp,
                cancellationToken: cancellationToken
           ).ConfigureAwait(false);
            builds = builds
                .Where(b => b.SourceBranch == "refs/heads/master"
                            && commit.Equals(b.SourceVersion, StringComparison.InvariantCultureIgnoreCase)
                            && b.Status == BuildStatus.Completed
                )
                .OrderByDescending(b => b.StartTime)
                .ToList();
            var latestBuild = builds.FirstOrDefault();
            if (latestBuild == null)
                return null;

            result = await azureDevOpsClient.GetArtifactsInfoAsync(commit, latestBuild, cancellationToken).ConfigureAwait(false);
            if (result.Status == BuildStatus.Completed && (result.Result == BuildResult.Succeeded || result.Result == BuildResult.PartiallySucceeded))
                BuildInfoCache.Set(commit, result, TimeSpan.FromHours(1));
            return result;
        }

        public static async Task<BuildInfo?> GetPrBuildInfoAsync(this BuildHttpClient? azureDevOpsClient, string? commit, DateTime? oldestTimestamp, int pr, CancellationToken cancellationToken)
        {
            if (azureDevOpsClient == null || string.IsNullOrEmpty(commit))
                return null;

            commit = commit.ToLower();
            if (BuildInfoCache.TryGetValue(commit, out BuildInfo result))
                return result;

            var builds = await azureDevOpsClient.GetBuildsAsync(
                Config.AzureDevOpsProjectId,
                repositoryId: "RPCS3/rpcs3",
                repositoryType: "GitHub",
                reasonFilter: BuildReason.PullRequest,
                minFinishTime: oldestTimestamp,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            var filterBranch = $"refs/pull/{pr}/merge";
            builds = builds
                .Where(b => b.SourceBranch == filterBranch
                            && b.TriggerInfo.TryGetValue("pr.sourceSha", out var trc)
                            && trc.Equals(commit, StringComparison.InvariantCultureIgnoreCase))
                .OrderByDescending(b => b.StartTime)
                .ToList();
            var latestBuild = builds.FirstOrDefault();
            if (latestBuild == null)
                return null;

            result = await azureDevOpsClient.GetArtifactsInfoAsync(commit, latestBuild, cancellationToken).ConfigureAwait(false);
            if (result.Status == BuildStatus.Completed && (result.Result == BuildResult.Succeeded || result.Result == BuildResult.PartiallySucceeded))
                BuildInfoCache.Set(commit, result, TimeSpan.FromHours(1));
            return result;
        }

        public static async Task<BuildInfo> GetArtifactsInfoAsync(this BuildHttpClient azureDevOpsClient, string commit, Build build, CancellationToken cancellationToken)
        {
            var result = new BuildInfo
            {
                Commit = commit,
                StartTime = build.StartTime,
                FinishTime = build.FinishTime,
                Status = build.Status,
                Result = build.Result,
            };
            var artifacts = await azureDevOpsClient.GetArtifactsAsync(Config.AzureDevOpsProjectId, build.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            // windows build
            var windowsBuildArtifact = artifacts.FirstOrDefault(a => a.Name.Contains("Windows"));
            var windowsBuild = windowsBuildArtifact?.Resource;
            if (windowsBuild?.DownloadUrl is string winDownloadUrl)
            {
                result.WindowsBuildDownloadLink = winDownloadUrl;
                if (windowsBuild.DownloadUrl.Contains("format=zip", StringComparison.InvariantCultureIgnoreCase))
                    try
                    {
                        using var httpClient = HttpClientFactory.Create();
                        await using var stream = await httpClient.GetStreamAsync(winDownloadUrl, cancellationToken).ConfigureAwait(false);
                        using var zipStream = ReaderFactory.Open(stream);
                        while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                        {
                            if (zipStream.Entry.Key.EndsWith(".7z", StringComparison.InvariantCultureIgnoreCase))
                            {
                                result.WindowsFilename = Path.GetFileName(zipStream.Entry.Key);
                                break;
                            }
                        }
                    }
                    catch (Exception e2)
                    {
                        Config.Log.Error(e2, "Failed to get windows build filename");
                    }
            }

            // linux build
            var linuxBuildArtifact = artifacts.FirstOrDefault(a => a.Name.EndsWith(".GCC")
                                                                   || a.Name.EndsWith("Linux")
                                                                   || a.Name.EndsWith("(gcc)")
                                                                   || a.Name.EndsWith("(clang)"));
            var linuxBuild = linuxBuildArtifact?.Resource;
            if (linuxBuild?.DownloadUrl is string linDownloadUrl)
            {
                result.LinuxBuildDownloadLink = linDownloadUrl;
                if (linuxBuild.DownloadUrl.Contains("format=zip", StringComparison.InvariantCultureIgnoreCase))
                    try
                    {
                        using var httpClient = HttpClientFactory.Create();
                        await using var stream = await httpClient.GetStreamAsync(linDownloadUrl, cancellationToken).ConfigureAwait(false);
                        using var zipStream = ReaderFactory.Open(stream);
                        while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                        {
                            if (zipStream.Entry.Key.EndsWith(".AppImage", StringComparison.InvariantCultureIgnoreCase))
                            {
                                result.LinuxFilename = Path.GetFileName(zipStream.Entry.Key);
                                break;
                            }
                        }
                    }
                    catch (Exception e2)
                    {
                        Config.Log.Error(e2, "Failed to get linux build filename");
                    }
            }
            return result;
        }
    }
}
