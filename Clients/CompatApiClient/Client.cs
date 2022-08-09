using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatApiClient.Formatters;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using Microsoft.Extensions.Caching.Memory;

namespace CompatApiClient;

public class Client: IDisposable
{
    private readonly HttpClient client;
    private readonly JsonSerializerOptions jsonOptions;
    private static readonly MemoryCache ResponseCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });

    public Client()
    {
        client = HttpClientFactory.Create(new CompressionMessageHandler());
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = SpecialJsonNamingPolicy.SnakeCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            IncludeFields = true,
            Converters = { new CompatApiCommitHashConverter(), },
        };
    }

    //todo: cache results
    public async Task<CompatResult?> GetCompatResultAsync(RequestBuilder requestBuilder, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var url = requestBuilder.Build();
        var tries = 0;
        do
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                try
                {
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    var result = await response.Content.ReadFromJsonAsync<CompatResult>(jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (result != null)
                    {
                        result.RequestBuilder = requestBuilder;
                        result.RequestDuration = DateTime.UtcNow - startTime;
                    }
                    return result;
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response, false);
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Warn(e);
            }
            tries++;
        } while (tries < 3);
        throw new HttpRequestException("Couldn't communicate with the API");
    }

    public async Task<CompatResult?> GetCompatListSnapshotAsync(CancellationToken cancellationToken)
    {
        var url = "https://rpcs3.net/compatibility?api=v1&export";
        if (ResponseCache.TryGetValue(url, out CompatResult? result))
            return result;

        var tries = 0;
        do
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                try
                {
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    result = await response.Content.ReadFromJsonAsync<CompatResult>(jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (result != null)
                        ResponseCache.Set(url, result, TimeSpan.FromDays(1));
                    return result;
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response, false);
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Warn(e);
            }
            tries++;
        } while (tries < 3);
        throw new HttpRequestException("Couldn't communicate with the API");
    }

    public async Task<UpdateInfo?> GetUpdateAsync(CancellationToken cancellationToken, string? commit = null)
    {
        if (string.IsNullOrEmpty(commit))
            commit = "somecommit";
        var tries = 3;
        do
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, "https://update.rpcs3.net/?api=v1&c=" + commit);
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
                try
                {
                    return await response.Content.ReadFromJsonAsync<UpdateInfo>(jsonOptions, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response, false);
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Warn(e);
            }
            tries++;
        } while (tries < 3);
        return null;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        client.Dispose();
    }
}