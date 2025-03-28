using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Utils;
using Microsoft.Extensions.Caching.Memory;
using Octokit;
using SharpCompress.Readers;

namespace GithubClient;

public partial class Client
{
    private static readonly MemoryCache BuildInfoCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

    public record BuildInfo
    {
        public required string Commit { get; init; }

        public string? WindowsFilename { get; init; }
        public string? LinuxFilename { get; init; }
        public string? MacFilename { get; init; }
        public string? WindowsArmFilename { get; init; }
        public string? LinuxArmFilename { get; init; }
        public string? MacArmFilename { get; init; }

        public string? WindowsBuildDownloadLink { get; init; }
        public string? LinuxBuildDownloadLink { get; init; }
        public string? MacBuildDownloadLink { get; init; }
        public string? WindowsArmBuildDownloadLink { get; init; }
        public string? LinuxArmBuildDownloadLink { get; init; }
        public string? MacArmBuildDownloadLink { get; init; }

        public DateTimeOffset StartTime { get; init; }
        public DateTimeOffset FinishTime { get; init; }

        public WorkflowRunStatus Status { get; init; }
        public WorkflowRunConclusion? Result { get; init; }
    }

    public record PipelineStats
    {
        public TimeSpan Percentile95 { get; init; }
        public TimeSpan Percentile90 { get; init; }
        public TimeSpan Percentile85 { get; init; }
        public TimeSpan Percentile80 { get; init; }
        public TimeSpan Mean { get; init; }
        public TimeSpan StdDev { get; init; }
        public int BuildCount { get; init; }

        public static readonly PipelineStats Defaults = new()
        {
            Percentile95 = TimeSpan.FromMinutes(32.1),
            Percentile90 = TimeSpan.FromMinutes(28.3),
            Percentile85 = TimeSpan.FromMinutes(26.2),
            Percentile80 = TimeSpan.FromMinutes(24.6),
            Mean = TimeSpan.FromMinutes(19.523423423333334),
            StdDev = TimeSpan.FromMinutes(6.374859008333333),
        };
    }

    private static long? workflowId = null;
    
    private async ValueTask<long?> GetWorkflowIdAsync()
    {
        if (workflowId.HasValue)
            return workflowId.Value;
        
        var workflows = await client.Actions.Workflows.List(OwnerId, RepoId).ConfigureAwait(false);
        var buildWf = workflows.Workflows.FirstOrDefault(wf => wf.Name is "Build RPCS3");
        if (buildWf is null)
            return null;
        
        return workflowId = buildWf.Id;
    }
    
    public async ValueTask<BuildInfo?> GetPrBuildInfoAsync(string commit, DateTime? oldestTimestamp, int pr, CancellationToken cancellationToken)
    {
        commit = commit.ToLower();
        if (BuildInfoCache.TryGetValue(commit, out BuildInfo? result) && result is not null)
            return result;

        if (await GetWorkflowIdAsync().ConfigureAwait(false) is not long wfId)
            return null;
        
        var wfrRequest = new WorkflowRunsRequest
        {
            ExcludePullRequests = false,
            Event = "pull_request",
            HeadSha = commit,
            //Branch = $"refs/pull/{pr}/merge",
        };
        var runsList = await client.Actions.Workflows.Runs.ListByWorkflow(OwnerId, RepoId, wfId, wfrRequest).ConfigureAwait(false);
        var builds = runsList.WorkflowRuns
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        var latestRun = builds.FirstOrDefault();
        if (latestRun is null)
            return null;

        result = await GetArtifactsInfoAsync(commit, latestRun, cancellationToken).ConfigureAwait(false);
        if (latestRun.Status.Value is WorkflowRunStatus.Completed)
            BuildInfoCache.Set(commit, result, TimeSpan.FromHours(1));
        return result;
    }
    
    public async ValueTask<BuildInfo> GetArtifactsInfoAsync(string commit, WorkflowRun run, CancellationToken cancellationToken)
    {
        var result = new BuildInfo
        {
            Commit = commit,
            StartTime = run.CreatedAt,
            FinishTime = run.UpdatedAt,
            Status = run.Status.Value,
            Result = run.Conclusion?.Value,
        };
        var artifactsList = await client.Actions.Artifacts.ListWorkflowArtifacts(OwnerId, RepoId, run.Id).ConfigureAwait(false);
        var artifacts = artifactsList.Artifacts;
        if (artifacts is not { Count: > 0 })
            return result;
        
        // windows build
        
        // gh api returns on these links:
        // https://api.github.com/repos/RPCS3/rpcs3/actions/artifacts/2802751674 /zip
        // we need public web links like this:
        // https://github.com/RPCS3/rpcs3/actions/runs/14017059654/artifacts/2802751674
        var windowsBuildArtifact = artifacts.FirstOrDefault(a => a.Name.Contains("Windows"));
        if (windowsBuildArtifact is { ArchiveDownloadUrl.Length: > 0, Expired: false })
        {
            var winZipUrl = $"https://github.com/RPCS3/rpcs3/actions/runs/{run.Id}/artifacts/{windowsBuildArtifact.Id}";
            result = result with { WindowsBuildDownloadLink = winZipUrl };
            try
            {
                await using var stream = await client.Actions.Artifacts.DownloadArtifact(OwnerId, RepoId, windowsBuildArtifact.Id, "zip").ConfigureAwait(false);
                using var zipStream = ReaderFactory.Open(stream);
                while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                {
                    if (zipStream.Entry.Key?.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        result = result with {WindowsFilename = Path.GetFileName(zipStream.Entry.Key)};
                        break;
                    }
                }
            }
            catch (Exception e2)
            {
                ApiConfig.Log.Error(e2, "Failed to get windows build filename");
            }
        }

        // linux build
        var linuxBuildArtifact = artifacts
            .Where(a => a.Name.Contains("Linux") && a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(a => a.Name.EndsWith("clang)", StringComparison.OrdinalIgnoreCase)
                                 || a.Name.EndsWith("gcc)", StringComparison.OrdinalIgnoreCase)
            );
        if (linuxBuildArtifact is { ArchiveDownloadUrl.Length: > 0, Expired: false })
        {
            var linZipUrl = $"https://github.com/RPCS3/rpcs3/actions/runs/{run.Id}/artifacts/{linuxBuildArtifact.Id}";
            result = result with { LinuxBuildDownloadLink = linZipUrl };
            try
            {
                await using var stream = await client.Actions.Artifacts.DownloadArtifact(OwnerId, RepoId, linuxBuildArtifact.Id, "zip").ConfigureAwait(false);
                using var zipStream = ReaderFactory.Open(stream);
                while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                {
                    if (zipStream.Entry.Key?.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        result = result with {LinuxFilename = Path.GetFileName(zipStream.Entry.Key)};
                        break;
                    }
                }
            }
            catch (Exception e2)
            {
                ApiConfig.Log.Error(e2, "Failed to get linux x64 build filename");
            }
        }

        // linux arm build
        var linuxArmBuildArtifact = artifacts
            .Where(a => a.Name.Contains("Linux") && a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(a => a.Name.EndsWith("clang)", StringComparison.OrdinalIgnoreCase)
                                 || a.Name.EndsWith("gcc)", StringComparison.OrdinalIgnoreCase)
            );
        if (linuxArmBuildArtifact is { ArchiveDownloadUrl.Length: > 0, Expired: false })
        {
            var linArmZipUrl = $"https://github.com/RPCS3/rpcs3/actions/runs/{run.Id}/artifacts/{linuxArmBuildArtifact.Id}";
            result = result with { LinuxArmBuildDownloadLink = linArmZipUrl };
            try
            {
                await using var stream = await client.Actions.Artifacts.DownloadArtifact(OwnerId, RepoId, linuxArmBuildArtifact.Id, "zip").ConfigureAwait(false);
                using var zipStream = ReaderFactory.Open(stream);
                while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                {
                    if (zipStream.Entry.Key?.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        result = result with {LinuxArmFilename = Path.GetFileName(zipStream.Entry.Key)};
                        break;
                    }
                }
            }
            catch (Exception e2)
            {
                ApiConfig.Log.Error(e2, "Failed to get linux arm build filename");
            }
        }

        // mac build
        var macBuildArtifact = artifacts.FirstOrDefault(a => a.Name.Contains("Mac") && a.Name.Contains("Intel"));
        if (macBuildArtifact is { ArchiveDownloadUrl.Length: > 0, Expired: false })
        {
            var macZipUrl = $"https://github.com/RPCS3/rpcs3/actions/runs/{run.Id}/artifacts/{macBuildArtifact.Id}";
            result = result with { MacBuildDownloadLink = macZipUrl };
            try
            {
                await using var stream = await client.Actions.Artifacts.DownloadArtifact(OwnerId, RepoId, macBuildArtifact.Id, "zip").ConfigureAwait(false);
                using var zipStream = ReaderFactory.Open(stream);
                while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                {
                    if (zipStream.Entry.Key?.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        result = result with { MacFilename = Path.GetFileName(zipStream.Entry.Key) };
                        break;
                    }
                }
            }
            catch (Exception e2)
            {
                ApiConfig.Log.Error(e2, "Failed to get mac x64 build filename");
            }
        }

        // mac arm build
        var macArmBuildArtifact = artifacts.FirstOrDefault(a => a.Name.Contains("Mac") && a.Name.Contains("Apple"));
        if (macArmBuildArtifact is { ArchiveDownloadUrl.Length: > 0, Expired: false })
        {
            var macArmZipUrl = $"https://github.com/RPCS3/rpcs3/actions/runs/{run.Id}/artifacts/{macArmBuildArtifact.Id}";
            result = result with { MacArmBuildDownloadLink = macArmZipUrl };
            try
            {
                await using var stream = await client.Actions.Artifacts.DownloadArtifact(OwnerId, RepoId, macArmBuildArtifact.Id, "zip").ConfigureAwait(false);
                using var zipStream = ReaderFactory.Open(stream);
                while (zipStream.MoveToNextEntry() && !cancellationToken.IsCancellationRequested)
                {
                    if (zipStream.Entry.Key?.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        result = result with { MacArmFilename = Path.GetFileName(zipStream.Entry.Key) };
                        break;
                    }
                }
            }
            catch (Exception e2)
            {
                ApiConfig.Log.Error(e2, "Failed to get mac arm build filename");
            }
        }

        return result;
    }
    
    public async Task<PipelineStats> GetPipelineDurationAsync(CancellationToken cancellationToken)
    {
        const string cacheKey = "pipeline-duration";
        if (BuildInfoCache.TryGetValue(cacheKey, out PipelineStats? result) && result is not null)
            return result;

        if (await GetWorkflowIdAsync().ConfigureAwait(false) is not long wfId)
            return PipelineStats.Defaults;

        var wfrRequest = new WorkflowRunsRequest
        {
            ExcludePullRequests = false,
            Status = CheckRunStatusFilter.Success,
            Created = $">{DateTime.UtcNow.AddDays(-7):yyyy-MM-ddTHH:mm:ssZ}"
        };
        var runsList = await client.Actions.Workflows.Runs.ListByWorkflow(OwnerId, RepoId, wfId, wfrRequest).ConfigureAwait(false);
        var times = runsList
            .WorkflowRuns
            .Select(b => (b.UpdatedAt - b.CreatedAt))
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
        BuildInfoCache.Set(cacheKey, result, TimeSpan.FromDays(1));
        return result;
    }
}