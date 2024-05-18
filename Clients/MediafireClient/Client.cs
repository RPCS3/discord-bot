using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CompatApiClient.Formatters;
using MediafireClient.POCOs;

namespace MediafireClient;

public sealed partial class Client
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions jsonOptions;
        
    //var optSecurityToken = "1605819132.376f3d84695f46daa7b69ee67fbc5edb0a00843a8b2d5ac7d3d1b1ad8a4212b0";
    //private static readonly Regex SecurityTokenRegex = new(@"(var\s+optSecurityToken|name=""security"" value)\s*=\s*""(?<security_token>.+)""", RegexOptions.ExplicitCapture);
    //var optDirectURL = "https://download1499.mediafire.com/12zqzob7gbfg/tmybrjpmtrpcejl/DemonsSouls_CrashLog_Nov.19th.zip";
    [GeneratedRegex(@"(var\s+optDirectURL|href)\s*=\s*""(?<direct_link>https?://download\d+\.mediafire\.com/.+)""")]
    private static partial Regex DirectUrlRegex();

    public Client()
    {
        client = HttpClientFactory.Create(new CompressionMessageHandler());
        jsonOptions = new()
        {
            PropertyNamingPolicy = SpecialJsonNamingPolicy.SnakeCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect)
            {
                var newLocation = response.Headers.Location;
                ApiConfig.Log.Warn($"Unexpected redirect from {webLink} to {newLocation}");
                return null;
            }
                
            try
            {
                await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var m = DirectUrlRegex().Match(html);
                if (m.Success)
                    return new(m.Groups["direct_link"].Value);
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