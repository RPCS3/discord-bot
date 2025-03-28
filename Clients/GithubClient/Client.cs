using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using Microsoft.Extensions.Caching.Memory;
using Octokit;

namespace GithubClient;

public partial class Client
{
    private readonly GitHubClient client;
    private static readonly HttpClient httpClient = HttpClientFactory.Create();

    private const string OwnerId = "RPCS3";
    private const string RepoId = "rpcs3";
    private static readonly TimeSpan PrStatusCacheTime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan IssueStatusCacheTime = TimeSpan.FromMinutes(30);
    private static readonly MemoryCache StatusesCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(1) });
    private static readonly MemoryCache IssuesCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(30) });

    public static int RateLimit { get; private set; }
    public static int RateLimitRemaining { get; private set; }
    public static DateTime RateLimitResetTime { get; private set; }

    public Client(string? githubToken)
    {
        client = new(new ProductHeaderValue(ApiConfig.ProductName, ApiConfig.ProductVersion));
        if (githubToken is {Length: >0})
            client.Credentials = new(githubToken);
    }

    public async ValueTask<PullRequest?> GetPrInfoAsync(int pr, CancellationToken cancellationToken)
    {
        if (StatusesCache.TryGetValue(pr, out PullRequest? result))
        {
            ApiConfig.Log.Debug($"Returned {nameof(PullRequest)} for {pr} from cache");
            return result;
        }

        try
        {
            result = await client.PullRequest.Get(OwnerId, RepoId, pr).WaitAsync(cancellationToken).ConfigureAwait(false);
            UpdateRateLimitStats();
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        if (result == null)
        {
            ApiConfig.Log.Debug($"Failed to get {nameof(PullRequest)}, returning empty result");
            return new(pr);
        }

        StatusesCache.Set(pr, result, PrStatusCacheTime);
        ApiConfig.Log.Debug($"Cached {nameof(PullRequest)} for {pr} for {PrStatusCacheTime}");
        return result;
    }

    public async ValueTask<Issue?> GetIssueInfoAsync(int issue, CancellationToken cancellationToken)
    {
        if (IssuesCache.TryGetValue(issue, out Issue? result))
        {
            ApiConfig.Log.Debug($"Returned {nameof(Issue)} for {issue} from cache");
            return result;
        }

        try
        {
            result = await client.Issue.Get(OwnerId, RepoId, issue).WaitAsync(cancellationToken).ConfigureAwait(false);
            UpdateRateLimitStats();
            IssuesCache.Set(issue, result, IssueStatusCacheTime);
            ApiConfig.Log.Debug($"Cached {nameof(Issue)} for {issue} for {IssueStatusCacheTime}");
            return result;
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        ApiConfig.Log.Debug($"Failed to get {nameof(Issue)}, returning empty result");
        return new();
    }

    public ValueTask<IReadOnlyList<PullRequest>?> GetOpenPrsAsync(CancellationToken cancellationToken)
        => GetPrsWithStatusAsync(new() { State = ItemStateFilter.Open }, cancellationToken);

    public ValueTask<IReadOnlyList<PullRequest>?> GetClosedPrsAsync(CancellationToken cancellationToken) => GetPrsWithStatusAsync(new()
    {
        State = ItemStateFilter.Closed,
        SortProperty = PullRequestSort.Updated,
        SortDirection = SortDirection.Descending
    }, cancellationToken);

    private async ValueTask<IReadOnlyList<PullRequest>?> GetPrsWithStatusAsync(PullRequestRequest filter, CancellationToken cancellationToken)
    {
        var statusUri = "https://api.github.com/repos/RPCS3/rpcs3/pulls?state=" + filter;
        if (StatusesCache.TryGetValue(statusUri, out IReadOnlyList<PullRequest>? result))
        {
            ApiConfig.Log.Debug("Returned list of opened PRs from cache");
            return result;
        }

        try
        {
            result = await client.PullRequest.GetAllForRepository(OwnerId, RepoId, filter).WaitAsync(cancellationToken).ConfigureAwait(false);
            UpdateRateLimitStats();
            StatusesCache.Set(statusUri, result, PrStatusCacheTime);
            foreach (var prInfo in result)
                StatusesCache.Set(prInfo.Number, prInfo, PrStatusCacheTime);
            ApiConfig.Log.Debug($"Cached list of open PRs for {PrStatusCacheTime}");
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        return result;
    }

    private void UpdateRateLimitStats()
    {
        var apiInfo = client.GetLastApiInfo();
        if (apiInfo is null)
            return;

        RateLimit = apiInfo.RateLimit.Limit;
        RateLimitRemaining = apiInfo.RateLimit.Remaining;
        RateLimitResetTime = DateTimeOffset.FromUnixTimeSeconds(apiInfo.RateLimit.ResetAsUtcEpochSeconds).UtcDateTime;
        if (RateLimitRemaining < 10)
            ApiConfig.Log.Warn($"Github rate limit is low: {RateLimitRemaining} out of {RateLimit}, will be reset on {RateLimitResetTime:u}");
    }
}