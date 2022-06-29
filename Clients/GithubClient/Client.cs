using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using Microsoft.Extensions.Caching.Memory;

namespace GithubClient;

public class Client
{
    private readonly Octokit.GitHubClient client;

    private static readonly TimeSpan PrStatusCacheTime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan IssueStatusCacheTime = TimeSpan.FromMinutes(30);
    private static readonly MemoryCache StatusesCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(1) });
    private static readonly MemoryCache IssuesCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(30) });

    public static int RateLimit { get; private set; }
    public static int RateLimitRemaining { get; private set; }
    public static DateTime RateLimitResetTime { get; private set; }

    public Client(string? githubToken)
    {
        client = new Octokit.GitHubClient(new Octokit.ProductHeaderValue(ApiConfig.ProductName, ApiConfig.ProductVersion));
        if (!string.IsNullOrEmpty(githubToken))
        {
            client.Credentials = new Octokit.Credentials(githubToken);
        }
    }

    public async Task<Octokit.PullRequest?> GetPrInfoAsync(int pr, CancellationToken cancellationToken)
    {
        if (StatusesCache.TryGetValue(pr, out Octokit.PullRequest? result))
        {
            ApiConfig.Log.Debug($"Returned {nameof(Octokit.PullRequest)} for {pr} from cache");
            return result;
        }

        try
        {
            var request = client.PullRequest.Get("RPCS3", "rpcs3", pr);
            request.Wait(cancellationToken);
            result = (await request.ConfigureAwait(false));
            UpdateRateLimitStats();
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        if (result == null)
        {
            ApiConfig.Log.Debug($"Failed to get {nameof(Octokit.PullRequest)}, returning empty result");
            return new(pr);
        }

        StatusesCache.Set(pr, result, PrStatusCacheTime);
        ApiConfig.Log.Debug($"Cached {nameof(Octokit.PullRequest)} for {pr} for {PrStatusCacheTime}");
        return result;
    }

    public async Task<Octokit.Issue?> GetIssueInfoAsync(int issue, CancellationToken cancellationToken)
    {
        if (IssuesCache.TryGetValue(issue, out Octokit.Issue? result))
        {
            ApiConfig.Log.Debug($"Returned {nameof(Octokit.Issue)} for {issue} from cache");
            return result;
        }

        try
        {
            var request = client.Issue.Get("RPCS3", "rpcs3", issue);
            request.Wait(cancellationToken);
            result = (await request.ConfigureAwait(false));
            UpdateRateLimitStats();
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        if (result == null)
        {
            ApiConfig.Log.Debug($"Failed to get {nameof(Octokit.Issue)}, returning empty result");
            return new() { };
        }

        IssuesCache.Set(issue, result, IssueStatusCacheTime);
        ApiConfig.Log.Debug($"Cached {nameof(Octokit.Issue)} for {issue} for {IssueStatusCacheTime}");
        return result;
    }

    public Task<IReadOnlyList<Octokit.PullRequest>?> GetOpenPrsAsync(CancellationToken cancellationToken) => GetPrsWithStatusAsync(new Octokit.PullRequestRequest
    {
        State = Octokit.ItemStateFilter.Open
    }, cancellationToken);

    public Task<IReadOnlyList<Octokit.PullRequest>?> GetClosedPrsAsync(CancellationToken cancellationToken) => GetPrsWithStatusAsync(new Octokit.PullRequestRequest
    {
        State = Octokit.ItemStateFilter.Closed,
        SortProperty = Octokit.PullRequestSort.Updated,
        SortDirection = Octokit.SortDirection.Descending
    }, cancellationToken);

    private async Task<IReadOnlyList<Octokit.PullRequest>?> GetPrsWithStatusAsync(Octokit.PullRequestRequest filter, CancellationToken cancellationToken)
    {
        var statusURI = "https://api.github.com/repos/RPCS3/rpcs3/pulls?state=" + filter.ToString();
        if (StatusesCache.TryGetValue(statusURI, out IReadOnlyList<Octokit.PullRequest>? result))
        {
            ApiConfig.Log.Debug("Returned list of opened PRs from cache");
            return result;
        }

        try
        {
            var request = client.PullRequest.GetAllForRepository("RPCS3", "rpcs3", filter);
            request.Wait(cancellationToken);

            result = (await request.ConfigureAwait(false));
            UpdateRateLimitStats();
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        if (result != null)
        {
            StatusesCache.Set(statusURI, result, PrStatusCacheTime);
            foreach (var prInfo in result)
                StatusesCache.Set(prInfo.Number, prInfo, PrStatusCacheTime);
            ApiConfig.Log.Debug($"Cached list of open PRs for {PrStatusCacheTime}");
        }
        return result;
    }

    private void UpdateRateLimitStats()
    {
        var apiInfo = client.GetLastApiInfo();
        if (apiInfo == null)
        {
            return;
        }

        RateLimit = apiInfo.RateLimit.Limit;
        RateLimitRemaining = apiInfo.RateLimit.Remaining;
        RateLimitResetTime = DateTimeOffset.FromUnixTimeSeconds(apiInfo.RateLimit.ResetAsUtcEpochSeconds).UtcDateTime;

        if (RateLimitRemaining < 10)
            ApiConfig.Log.Warn($"Github rate limit is low: {RateLimitRemaining} out of {RateLimit}, will be reset on {RateLimitResetTime:u}");
    }

}