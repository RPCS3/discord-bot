using System;
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
        private static readonly MemoryCache BuildInfoCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

        public class BuildInfo
        {
            public string WindowsFilename;
            public string LinuxFilename;
            public string WindowsBuildDownloadLink;
            public string LinuxBuildDownloadLink;
            public DateTime? FinishTime;
            public BuildStatus? Status;
        }

        public static async Task<BuildInfo> GetMasterBuildInfoAsync(this BuildHttpClient azureDevOpsClient, string commit, DateTime? oldestTimestamp, CancellationToken cancellationToken)
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
                maxFinishTime: oldestTimestamp,
                cancellationToken: cancellationToken
           ).ConfigureAwait(false);
            builds = builds
                .Where(b => b.SourceBranch == "refs/heads/master" && commit.Equals(b.SourceVersion, StringComparison.InvariantCultureIgnoreCase))
                .OrderByDescending(b => b.StartTime)
                .ToList();
            var latestBuild = builds.FirstOrDefault();
            if (latestBuild == null)
                return null;

            result = await azureDevOpsClient.GetArtifactsInfoAsync(latestBuild, cancellationToken).ConfigureAwait(false);
            if (result.Status == BuildStatus.Completed)
                BuildInfoCache.Set(commit, result, TimeSpan.FromDays(1));
            return result;
        }

        public static async Task<BuildInfo> GetPrBuildInfoAsync(this BuildHttpClient azureDevOpsClient, string commit, DateTime? oldestTimestamp, int pr, CancellationToken cancellationToken)
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
                maxFinishTime: oldestTimestamp,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
            var filterBranch = $"refs/pull/{pr}/merge";
            builds = builds
                .Where(b => b.SourceBranch == filterBranch && b.TriggerInfo.TryGetValue("pr.sourceSha", out var trc) && trc.Equals(commit, StringComparison.InvariantCultureIgnoreCase))
                .OrderByDescending(b => b.StartTime)
                .ToList();
            var latestBuild = builds.FirstOrDefault();
            if (latestBuild == null)
                return null;

            result = await azureDevOpsClient.GetArtifactsInfoAsync(latestBuild, cancellationToken).ConfigureAwait(false);
            if (result.Status == BuildStatus.Completed)
                BuildInfoCache.Set(commit, result, TimeSpan.FromDays(1));
            return result;
        }

        public static async Task<BuildInfo> GetArtifactsInfoAsync(this BuildHttpClient azureDevOpsClient, Build build, CancellationToken cancellationToken)
        {
            if (azureDevOpsClient == null)
                return null;

            var result = new BuildInfo
            {
                FinishTime = build.FinishTime,
                Status = build.Status,
            };
            var artifacts = await azureDevOpsClient.GetArtifactsAsync(Config.AzureDevOpsProjectId, build.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            // windows build
            var windowsBuildArtifact = artifacts.FirstOrDefault(a => a.Name.StartsWith("Windows"));
            var windowsBuild = windowsBuildArtifact?.Resource;
            if (windowsBuild?.DownloadUrl is string winDownloadUrl)
            {
                result.WindowsBuildDownloadLink = winDownloadUrl;
                if (windowsBuild.DownloadUrl.Contains("format=zip", StringComparison.InvariantCultureIgnoreCase))
                    try
                    {
                        using var httpClient = HttpClientFactory.Create();
                        using var stream = await httpClient.GetStreamAsync(winDownloadUrl).ConfigureAwait(false);
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
            var linuxBuildArtifact = artifacts.FirstOrDefault(a => a.Name.EndsWith(".GCC"));
            var linuxBuild = linuxBuildArtifact?.Resource;
            if (linuxBuild?.DownloadUrl is string linDownloadUrl)
            {
                result.LinuxBuildDownloadLink = linDownloadUrl;
                if (linuxBuild.DownloadUrl.Contains("format=zip", StringComparison.InvariantCultureIgnoreCase))
                    try
                    {
                        using var httpClient = HttpClientFactory.Create();
                        using var stream = await httpClient.GetStreamAsync(linDownloadUrl).ConfigureAwait(false);
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
