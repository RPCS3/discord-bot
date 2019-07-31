using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using GithubClient.POCOs;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using JsonContractResolver = CompatApiClient.JsonContractResolver;

namespace GithubClient
{
    public class Client
    {
        private readonly HttpClient client;
        private readonly MediaTypeFormatterCollection formatters;

        private static readonly ProductInfoHeaderValue ProductInfoHeader = new ProductInfoHeaderValue("RPCS3CompatibilityBot", "2.0");
        private static readonly TimeSpan PrStatusCacheTime = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IssueStatusCacheTime = TimeSpan.FromMinutes(30);
        private static readonly MemoryCache StatusesCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(1) });
        private static readonly MemoryCache IssuesCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromMinutes(30) });

        public static int RateLimit { get; private set; }
        public static int RateLimitRemaining { get; private set; }
        public static DateTime RateLimitResetTime { get; private set; }

        public Client()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new JsonContractResolver(NamingStyles.Underscore),
                NullValueHandling = NullValueHandling.Ignore
            };
            formatters = new MediaTypeFormatterCollection(new[] { new JsonMediaTypeFormatter { SerializerSettings = settings } });
        }

        public async Task<PrInfo> GetPrInfoAsync(int pr, CancellationToken cancellationToken)
        {
            if (StatusesCache.TryGetValue(pr, out PrInfo result))
            {
                ApiConfig.Log.Debug($"Returned {nameof(PrInfo)} for {pr} from cache");
                return result;
            }

            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/RPCS3/rpcs3/pulls/" + pr))
                {
                    message.Headers.UserAgent.Add(ProductInfoHeader);
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            UpdateRateLimitStats(response.Headers);
                            result = await response.Content.ReadAsAsync<PrInfo>(formatters, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (result == null)
            {
                ApiConfig.Log.Debug($"Failed to get {nameof(PrInfo)}, returning empty result");
                return new PrInfo { Number = pr };
            }

            StatusesCache.Set(pr, result, PrStatusCacheTime);
            ApiConfig.Log.Debug($"Cached {nameof(PrInfo)} for {pr} for {PrStatusCacheTime}");
            return result;
        }

        public async Task<IssueInfo> GetIssueInfoAsync(int issue, CancellationToken cancellationToken)
        {
            if (IssuesCache.TryGetValue(issue, out IssueInfo result))
            {
                ApiConfig.Log.Debug($"Returned {nameof(IssueInfo)} for {issue} from cache");
                return result;
            }

            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/RPCS3/rpcs3/issues/" + issue))
                {
                    message.Headers.UserAgent.Add(ProductInfoHeader);
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            UpdateRateLimitStats(response.Headers);
                            result = await response.Content.ReadAsAsync<IssueInfo>(formatters, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (result == null)
            {
                ApiConfig.Log.Debug($"Failed to get {nameof(IssueInfo)}, returning empty result");
                return new IssueInfo { Number = issue };
            }

            IssuesCache.Set(issue, result, IssueStatusCacheTime);
            ApiConfig.Log.Debug($"Cached {nameof(IssueInfo)} for {issue} for {IssueStatusCacheTime}");
            return result;
        }

        public async Task<List<PrInfo>> GetOpenPrsAsync(CancellationToken cancellationToken)
        {
            var requestUri = "https://api.github.com/repos/RPCS3/rpcs3/pulls?state=open";
            if (StatusesCache.TryGetValue(requestUri, out List<PrInfo> result))
            {
                ApiConfig.Log.Debug("Returned list of opened PRs from cache");
                return result;
            }

            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    message.Headers.UserAgent.Add(ProductInfoHeader);
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            UpdateRateLimitStats(response.Headers);
                            result = await response.Content.ReadAsAsync<List<PrInfo>>(formatters, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response);
                        }
                    }
                }
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
                using (var message = new HttpRequestMessage(HttpMethod.Get, statusesUrl))
                {
                    message.Headers.UserAgent.Add(ProductInfoHeader);
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            UpdateRateLimitStats(response.Headers);
                            result = await response.Content.ReadAsAsync<List<StatusInfo>>(formatters, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            ConsoleLogger.PrintError(e, response);
                        }
                    }
                }
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

        private static void UpdateRateLimitStats(HttpResponseHeaders headers)
        {
            if (headers.TryGetValues("X-RateLimit-Limit", out var rateLimitValues)
                && rateLimitValues?.FirstOrDefault() is string limitValue
                && int.TryParse(limitValue, out var limit)
                && limit > 0)
                RateLimit = limit;
            if (headers.TryGetValues("X-RateLimit-Remaining", out var rateLimitRemainingValues)
                && rateLimitRemainingValues?.FirstOrDefault() is string remainingValue
                && int.TryParse(remainingValue, out var remaining)
                && remaining > 0)
                RateLimitRemaining = remaining;
            if (headers.TryGetValues("X-RateLimit-Reset", out var rateLimitResetValues)
                && rateLimitResetValues?.FirstOrDefault() is string resetValue
                && long.TryParse(resetValue, out var resetSeconds)
                && resetSeconds > 0)
                RateLimitResetTime = DateTimeOffset.FromUnixTimeSeconds(resetSeconds).UtcDateTime;
            if (RateLimitRemaining < 10)
                ApiConfig.Log.Warn($"Github rate limit is low: {RateLimitRemaining} out of {RateLimit}, will be reset on {RateLimitResetTime:u}");
        }
    }
}
