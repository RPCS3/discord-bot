using System;
using System.Collections.Generic;
using System.IO;
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
                if (!int.TryParse(new Uri(githubStatusTargetUrl).Segments.Last(), out var buildNumber))
                    return null;

                var buildInfo = await GetBuildInfoAsync(buildNumber, cancellationToken).ConfigureAwait(false);
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
                return result;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            ResponseCache.TryGetValue(githubStatusTargetUrl, out var o);
            return o as ArtifactInfo;
        }

        public async Task<BuildInfo> GetBuildInfoAsync(int buildNumber, CancellationToken cancellationToken)
        {
            var requestUri = "https://ci.appveyor.com/api/projects/rpcs3/rpcs3/builds/" + buildNumber;
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
                            var result =  await response.Content.ReadAsAsync<BuildInfo>(formatters, cancellationToken).ConfigureAwait(false);
                            ResponseCache.Set(requestUri, result, CacheTime);
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
            ResponseCache.TryGetValue(requestUri, out var o);
            return o as BuildInfo;
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
            ResponseCache.TryGetValue(requestUri, out var o);
            return o as List<Artifact>;
        }
    }
}
