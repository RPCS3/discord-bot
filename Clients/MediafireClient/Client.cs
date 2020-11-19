using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompatApiClient.Formatters;
using MediafireClient.POCOs;

namespace MediafireClient
{
    public class Client
    {
        private readonly HttpClient client;
        private readonly HttpClient noRedirectsClient;
        private readonly JsonSerializerOptions jsonOptions;
        
        //var optSecurityToken = "1605819132.376f3d84695f46daa7b69ee67fbc5edb0a00843a8b2d5ac7d3d1b1ad8a4212b0";
        private static readonly Regex SecurityTokenRegex = new(@"(var\s+optSecurityToken|name=""security"" value)\s*=\s*""(?<security_token>.+)""", RegexOptions.ExplicitCapture);
        //var optDirectURL = "https://download1499.mediafire.com/12zqzob7gbfg/tmybrjpmtrpcejl/DemonsSouls_CrashLog_Nov.19th.zip";
        private static readonly Regex DirectUrlRegex = new(@"(var\s+optDirectURL|href)\s*=\s*""(?<direct_link>https?://download\d+\.mediafire\.com/.+)""");

        public Client()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            noRedirectsClient = HttpClientFactory.Create(new HttpClientHandler {AllowAutoRedirect = false});
            jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = SpecialJsonNamingPolicy.SnakeCase,
                IgnoreNullValues = true,
                IncludeFields = true,
            };
        }

        public async Task<LinksResult?> GetWebLinkAsync(string quickKey, CancellationToken cancellationToken)
        {
            try
            {
                var uri = new Uri($"https://www.mediafire.com/api/1.5/file/get_links.php?quick_key={quickKey}&response_format=json");
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.UserAgent.Add(ApiConfig.ProductInfoHeader);
                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                try
                {
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    return await response.Content.ReadFromJsonAsync<LinksResult>(jsonOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response);
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            return null;
        }

        public async Task<Uri?> GetDirectDownloadLinkAsync(Uri webLink, CancellationToken cancellationToken)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, webLink);
                message.Headers.UserAgent.Add(ApiConfig.ProductInfoHeader);
                using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
                try
                {
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var m = DirectUrlRegex.Match(html);
                    if (m.Success)
                        return new Uri(m.Groups["direct_link"].Value);
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response);
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
            }
            return null;
        }
    }
}