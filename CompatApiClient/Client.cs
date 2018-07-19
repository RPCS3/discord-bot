using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatApiClient.POCOs;
using Newtonsoft.Json;

namespace CompatApiClient
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
            formatters = new MediaTypeFormatterCollection(new[] {new JsonMediaTypeFormatter {SerializerSettings = settings}});
        }

        //todo: cache results
        public async Task<CompatResult> GetCompatResultAsync(RequestBuilder requestBuilder, CancellationToken cancellationToken)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, requestBuilder.Build());
            var startTime = DateTime.UtcNow;
            CompatResult result;
            try
            {
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                result = await response.Content.ReadAsAsync<CompatResult>(formatters, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PrintError(e);
                result = new CompatResult{ReturnCode = -2};
            }
            result.RequestBuilder = requestBuilder;
            result.RequestDuration = DateTime.UtcNow - startTime;
            return result;
        }

        public async Task<UpdateInfo> GetUpdateAsync(CancellationToken cancellationToken, string commit = "somecommit")
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "https://update.rpcs3.net/?c=" + commit);
            UpdateInfo result;
            try
            {
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                result = await response.Content.ReadAsAsync<UpdateInfo>(formatters, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PrintError(e);
                result = new UpdateInfo { ReturnCode = -2 };
            }
            return result;
        }

        public async Task<PrInfo> GetPrInfoAsync(string pr, CancellationToken cancellationToken)
        {
            if (prInfoCache.TryGetValue(pr, out var result))
                return result;

            var message = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/RPCS3/rpcs3/pulls/" + pr);
            HttpContent content = null;
            try
            {
                message.Headers.UserAgent.Add(new ProductInfoHeaderValue("RPCS3CompatibilityBot", "2.0"));
                var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                content = response.Content;
                await content.LoadIntoBufferAsync().ConfigureAwait(false);
                result = await content.ReadAsAsync<PrInfo>(formatters, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                PrintError(e);
                if (content != null)
                    try { Console.WriteLine(await content.ReadAsStringAsync().ConfigureAwait(false)); } catch {}
                int.TryParse(pr, out var prnum);
                return new PrInfo{Number = prnum};
            }

            lock (prInfoCache)
            {
                if (prInfoCache.TryGetValue(pr, out var cachedResult))
                    return cachedResult;

                prInfoCache[pr] = result;
                return result;
            }
        }

        private void PrintError(Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error communicating with api: " + e.Message);
            Console.ResetColor();
        }
    }
}