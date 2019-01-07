using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using GithubClient.POCOs;
using Newtonsoft.Json;
using JsonContractResolver = CompatApiClient.JsonContractResolver;

namespace GithubClient
{
    public class Client
    {
        private readonly HttpClient client;
        private readonly MediaTypeFormatterCollection formatters;

        private static readonly Dictionary<string, PrInfo> prInfoCache = new Dictionary<string, PrInfo>();

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

        public async Task<PrInfo> GetPrInfoAsync(string pr, CancellationToken cancellationToken)
        {
            if (prInfoCache.TryGetValue(pr, out var result))
                return result;

            try
            {
                using (var message = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/RPCS3/rpcs3/pulls/" + pr))
                {
                    message.Headers.UserAgent.Add(new ProductInfoHeaderValue("RPCS3CompatibilityBot", "2.0"));
                    using (var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
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
                int.TryParse(pr, out var prnum);
                return new PrInfo { Number = prnum };
            }

            lock (prInfoCache)
            {
                if (prInfoCache.TryGetValue(pr, out var cachedResult))
                    return cachedResult;

                prInfoCache[pr] = result;
                return result;
            }
        }
    }
}
