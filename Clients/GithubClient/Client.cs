using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using Microsoft.Extensions.Caching.Memory;
using Octokit.GraphQL;
using Octokit.GraphQL.Model;

namespace GithubClient
{
    public class Client
    {
        private static readonly ProductHeaderValue ProductInfoHeader = new ProductHeaderValue("RPCS3CompatibilityBot", "2.0");
        private static readonly Connection Connection = new Connection(ProductInfoHeader, "YOUR_OAUTH_TOKEN");
        private static readonly TimeSpan PrStatusCacheTime = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IssueStatusCacheTime = TimeSpan.FromMinutes(30);
        private static readonly MemoryCache StatusesCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(1) });
        private static readonly MemoryCache IssuesCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(30) });

        public static int RateLimit { get; private set; }
        public static int RateLimitRemaining { get; private set; }
        public static DateTime RateLimitResetTime { get; private set; }

        public async Task<PullRequest> GetPrInfoAsync(int pr, CancellationToken cancellationToken)
        {
            if (StatusesCache.TryGetValue(pr, out PullRequest result))
            {
                ApiConfig.Log.Debug($"Returned {nameof(PullRequest)} for {pr} from cache");
                return result;
            }

            try
            {
                var query = new Query()
                    .RepositoryOwner("RPCS3")
                    .Repository("rpcs3")
                    .PullRequest(pr);

                result = await Connection.Run(query, cancellationToken);

                UpdateRateLimitStats();
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (result == null)
            {
                ApiConfig.Log.Debug($"Failed to get {nameof(PullRequest)}, returning empty result");

                return null;
            }

            StatusesCache.Set(pr, result, PrStatusCacheTime);
            ApiConfig.Log.Debug($"Cached {nameof(PullRequest)} for {pr} for {PrStatusCacheTime}");
            return result;
        }

        public async Task<Issue> GetIssueInfoAsync(int issue, CancellationToken cancellationToken)
        {
            if (IssuesCache.TryGetValue(issue, out Issue result))
            {
                ApiConfig.Log.Debug($"Returned {nameof(Issue)} for {issue} from cache");
                return result;
            }

            try
            {
                var query = new Query()
                    .RepositoryOwner("RPCS3")
                    .Repository("rpcs3")
                    .Issue(issue);

                result = await Connection.Run(query, cancellationToken);

                UpdateRateLimitStats();
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (result == null)
            {
                ApiConfig.Log.Debug($"Failed to get {nameof(Issue)}, returning empty result");

                return null;
            }

            IssuesCache.Set(issue, result, IssueStatusCacheTime);
            ApiConfig.Log.Debug($"Cached {nameof(Issue)} for {issue} for {IssueStatusCacheTime}");
            return result;
        }

        public async Task<List<PullRequest>> GetOpenPrsAsync(CancellationToken cancellationToken)
        {
            var requestUri = "https://api.github.com/repos/RPCS3/rpcs3/pulls?state=open";
            if (StatusesCache.TryGetValue(requestUri, out List<PullRequest> result))
            {
                ApiConfig.Log.Debug("Returned list of opened PRs from cache");
                return result;
            }

            try
            {
                var query = new Query()
                    .RepositoryOwner("RPCS3")
                    .Repository("rpcs3")
                    .PullRequests()
                    .Nodes;

                result = (await Connection.Run(query, cancellationToken)).ToList();

                UpdateRateLimitStats();
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (result != null)
            {
                StatusesCache.Set(requestUri, result, PrStatusCacheTime);
                ApiConfig.Log.Debug($"Cached list of open PRs for {PrStatusCacheTime}");
            }
            return result;
        }

        public async Task<List<StatusInfo>> GetStatusesAsync(string statusesUrl, CancellationToken cancellationToken)
        {
            if (StatusesCache.TryGetValue(statusesUrl, out List<StatusInfo> result))
            {
                ApiConfig.Log.Debug($"Returned cached item for {statusesUrl}");
                return result;
            }

            try
            {
                var query = new Query()
                    .RepositoryOwner("RPCS3")
                    .Repository("rpcs3")
                    .PullRequest("")
                    .Commits(1);
                    

                result = (await Connection.Run(query, cancellationToken)).ToList();

                UpdateRateLimitStats();
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }

            if (result != null)
            {
                StatusesCache.Set(statusesUrl, result, PrStatusCacheTime);
                ApiConfig.Log.Debug($"Cached item for {statusesUrl} for {PrStatusCacheTime}");
            }
            return result;
        }

        private async void UpdateRateLimitStats()
        {
            var query = new Query()
                .RateLimit();

            var rateLimit = await Connection.Run(query);
            
            RateLimit = rateLimit.Limit;
            RateLimitRemaining = rateLimit.Remaining;
            RateLimitResetTime = rateLimit.ResetAt.UtcDateTime;

            if (RateLimitRemaining < 10)
                ApiConfig.Log.Warn($"Github rate limit is low: {RateLimitRemaining} out of {RateLimit}, will be reset on {RateLimitResetTime:u}");
        }
    }
}
