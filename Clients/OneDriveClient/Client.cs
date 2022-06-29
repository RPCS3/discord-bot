using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
using OneDriveClient.POCOs;

namespace OneDriveClient;

public class Client
{
    private readonly HttpClient client;
    private readonly HttpClient noRedirectsClient;
    private readonly JsonSerializerOptions jsonOptions;

    public Client()
    {
        client = HttpClientFactory.Create(new CompressionMessageHandler());
        noRedirectsClient = HttpClientFactory.Create(new HttpClientHandler {AllowAutoRedirect = false});
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
        };
    }

    private async Task<Uri?> ResolveShortLink(Uri shortLink, CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Head, shortLink);
            message.Headers.UserAgent.Add(ApiConfig.ProductInfoHeader);
            using var response = await noRedirectsClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.Headers.Location;
        }
        catch (Exception e)
        {
            ApiConfig.Log.Error(e);
        }
        return null;
    }

    // https://1drv.ms/u/s!AruI8iDXabVJ1ShAMIqxgU2tiHZ3 redirects to https://onedrive.live.com/redir?resid=49B569D720F288BB!10920&authkey=!AEAwirGBTa2Idnc
    // https://onedrive.live.com/?authkey=!AEAwirGBTa2Idnc&cid=49B569D720F288BB&id=49B569D720F288BB!10920&parId=49B569D720F288BB!4371&o=OneUp
    public async Task<DriveItemMeta?> ResolveContentLinkAsync(Uri? shareLink, CancellationToken cancellationToken)
    {
        if (shareLink?.Host == "1drv.ms")
            shareLink = await ResolveShortLink(shareLink, cancellationToken).ConfigureAwait(false);
        if (shareLink is null)
            return null;

        var queryParams = shareLink.ParseQueryString();
        string resourceId, authKey;
        if (queryParams["resid"] is string resId && queryParams["authkey"] is string akey)
        {
            resourceId = resId;
            authKey = akey;
        }
        else if (queryParams["id"] is string rid && queryParams["authkey"] is string aukey)
        {
            resourceId = rid;
            authKey = aukey;
        }
        else
        {
            ApiConfig.Log.Warn("Unknown or invalid OneDrive resource link: " + shareLink);
            return null;
        }

        var itemId = resourceId.Split('!')[0];
        try
        {
            var resourceMetaUri = new Uri($"https://api.onedrive.com/v1.0/drives/{itemId}/items/{resourceId}")
                .SetQueryParameters(
                    ("authkey", authKey),
                    ("select", "id,@content.downloadUrl,name,size")
                );
            using var message = new HttpRequestMessage(HttpMethod.Get, resourceMetaUri);
            message.Headers.UserAgent.Add(ApiConfig.ProductInfoHeader);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            try
            {
                await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                var meta = await response.Content.ReadFromJsonAsync<DriveItemMeta>(jsonOptions, cancellationToken).ConfigureAwait(false);
                if (meta?.ContentDownloadUrl is null)
                    throw new InvalidOperationException("Failed to properly deserialize response body");

                return meta;
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