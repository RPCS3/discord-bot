using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AppveyorClient.POCOs;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using JsonContractResolver = CompatApiClient.JsonContractResolver;

namespace AppveyorClient
{
    public class Client
    {
        private readonly HttpClient client;
        private readonly MediaTypeFormatterCollection formatters;

        private static readonly ProductInfoHeaderValue ProductInfoHeader = new ProductInfoHeaderValue("RPCS3CompatibilityBot", "2.0");
        private static readonly TimeSpan CacheTime = TimeSpan.FromDays(1);
        private static readonly MemoryCache ResponseCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

        public Client()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new JsonContractResolver(NamingStyles.CamelCase),
                NullValueHandling = NullValueHandling.Ignore
            };
            formatters = new MediaTypeFormatterCollection(new[] { new JsonMediaTypeFormatter { SerializerSettings = settings } });
        }

        public async Task<ArtifactInfo> GetPrDownloadAsync(string githubStatusTargetUrl, CancellationToken cancellationToken)
        {
            try
            {
                var buildUrl = githubStatusTargetUrl.Replace("ci.appveyor.com/project/", "ci.appveyor.com/api/projects/");
                if (buildUrl == githubStatusTargetUrl)
                {
                    ApiConfig.Log.Warn("Unexpected AppVeyor link: " + githubStatusTargetUrl);
                    return null;
                }

                var buildInfo = await GetBuildInfoAsync(buildUrl, cancellationToken).ConfigureAwait(false);
                var job = buildInfo?.Build.Jobs?.FirstOrDefault(j => j.Status == "success");
                if (string.IsNullOrEmpty(job?.JobId))
                    return null;

                var artifacts = await GetJobArtifactsAsync(job.JobId, cancellationToken).ConfigureAwait(false);
                var rpcs3Build = artifacts?.FirstOrDefault(a => a.Name == "rpcs3");
                if (rpcs3Build == null)
                    return null;

                var result = new ArtifactInfo
                {
                    Artifact = rpcs3Build,
                    DownloadUrl = $"https://ci.appveyor.com/api/buildjobs/{job.JobId}/artifacts/{rpcs3Build.FileName}",
                };
                ResponseCache.Set(githubStatusTargetUrl, result, CacheTime);
                ApiConfig.Log.Debug($"Cached item for {githubStatusTargetUrl} for {CacheTime}");
                return result;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            if (ResponseCache.TryGetValue(githubStatusTargetUrl, out ArtifactInfo o))
                ApiConfig.Log.Debug($"Returned cached item for {githubStatusTargetUrl}");
            return o;
        }

        public async Task<ArtifactInfo> GetPrDownloadAsync(int prNumber, DateTime dateTimeLimit, CancellationToken cancellationToken)
        {
            if (ResponseCache.TryGetValue(prNumber, out ArtifactInfo result))
            {
                ApiConfig.Log.Debug($"Returned cached {nameof(ArtifactInfo)} for {prNumber}");
                return result;
            }

            try
            {
                var baseUrl = new Uri("https://ci.appveyor.com/api/projects/rpcs3/RPCS3/history?recordsNumber=100");
                var historyUrl = baseUrl;
                HistoryInfo historyPage = null;
                Build build = null;
                do
                {
                    using (var message = new HttpRequestMessage(HttpMethod.Get, historyUrl))
                    {
                        message.Headers.UserAgent.Add(ProductInfoHeader);
                        using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                        {
                            try
                            {
                                await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                                historyPage = await response.Content.ReadAsAsync<HistoryInfo>(formatters, cancellationToken).ConfigureAwait(false);
                                build = historyPage.Builds.FirstOrDefault(b => b.PullRequestId == prNumber && b.Status == "success");
                            }
                            catch (Exception e)
                            {
                                ConsoleLogger.PrintError(e, response);
                                break;
                            }
                        }
                    }
                    historyUrl = baseUrl.SetQueryParameter("startBuildId", historyPage.Builds.Last().BuildId.ToString());
                } while (build == null && historyPage.Builds?.Count > 0 && historyPage.Builds.Last(b => b.Started.HasValue).Started > dateTimeLimit);

                var buildInfo = await GetBuildInfoAsync(build.BuildId, cancellationToken).ConfigureAwait(false);
                var job = buildInfo?.Build.Jobs?.FirstOrDefault(j => j.Status == "success");
                if (string.IsNullOrEmpty(job?.JobId))
                {
                    ApiConfig.Log.Debug($"No successful {nameof(Job.JobId)}");
                    return null;
                }

                var artifacts = await GetJobArtifactsAsync(job.JobId, cancellationToken).ConfigureAwait(false);
                var rpcs3Build = artifacts?.FirstOrDefault(a => a.Name == "rpcs3");
                if (rpcs3Build == null)
                {
                    ApiConfig.Log.Debug("No rpcs3 artifacts");
                    return null;
                }

                result = new ArtifactInfo
                {
                    Artifact = rpcs3Build,
                    DownloadUrl = $"https://ci.appveyor.com/api/buildjobs/{job.JobId}/artifacts/{rpcs3Build.FileName}",
                };
                ResponseCache.Set(prNumber, result, CacheTime);
                ApiConfig.Log.Debug($"Cached {nameof(ArtifactInfo)} for {prNumber} for {CacheTime}");
                return result;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            ApiConfig.Log.Debug($"Failed to get {nameof(ArtifactInfo)} for {prNumber}");
            return null;
        }

        private Task<BuildInfo> GetBuildInfoAsync(int buildId, CancellationToken cancellationToken)
        {
            return GetBuildInfoAsync("https://ci.appveyor.com/api/projects/rpcs3/rpcs3/builds/" + buildId, cancellationToken);
        }

        private async Task<BuildInfo> GetBuildInfoAsync(string buildUrl, CancellationToken cancellationToken)
        {
            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, buildUrl))
                {
                    message.Headers.UserAgent.Add(ProductInfoHeader);
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                            var result =  await response.Content.ReadAsAsync<BuildInfo>(formatters, cancellationToken).ConfigureAwait(false);
                            ResponseCache.Set(buildUrl, result, CacheTime);
                            ApiConfig.Log.Debug($"Cached {nameof(BuildInfo)} for {buildUrl} for {CacheTime}");
                            return result;
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
            if (ResponseCache.TryGetValue(buildUrl, out BuildInfo o))
                ApiConfig.Log.Debug($"Returning cached {nameof(BuildInfo)} for {buildUrl}");
            return o;
        }

        public async Task<List<Artifact>> GetJobArtifactsAsync(string jobId, CancellationToken cancellationToken)
        {
            var requestUri = $"https://ci.appveyor.com/api/buildjobs/{jobId}/artifacts";
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
                            var result =  await response.Content.ReadAsAsync<List<Artifact>>(formatters, cancellationToken).ConfigureAwait(false);
                            ResponseCache.Set(requestUri, result, CacheTime);
                            ApiConfig.Log.Debug($"Caching {nameof(Artifact)} for {jobId} for {CacheTime}");
                            return result;
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
            if (ResponseCache.TryGetValue(requestUri, out List<Artifact> o))
                ApiConfig.Log.Debug($"Returning cached {nameof(Artifact)} for {jobId}");
            return o;
        }

    }
}
