using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Formatters;
using CompatApiClient.Utils;
using YandexDiskClient.POCOs;

namespace YandexDiskClient;

public sealed class Client
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions jsonOptions;

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

    public Task<ResourceInfo?> GetResourceInfoAsync(string shareKey, CancellationToken cancellationToken)
        => GetResourceInfoAsync(new Uri($"https://yadi.sk/d/{shareKey}"), cancellationToken);
        
    public async Task<ResourceInfo?> GetResourceInfoAsync(Uri publicUri, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri("https://cloud-api.yandex.net/v1/disk/public/resources").SetQueryParameters(
                ("public_key", publicUri.ToString()),
                ("fields", "size,name,file")
            );
            using var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.UserAgent.Add(ApiConfig.ProductInfoHeader);
            using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            try
            {
                await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                return await response.Content.ReadFromJsonAsync<ResourceInfo>(jsonOptions, cancellationToken).ConfigureAwait(false);
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