﻿using System;
using System.Collections.Generic;
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
    private static readonly MemoryCache ResponseCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
    private static readonly string[] BuildArchList = [ArchType.X64, ArchType.Arm];

    private readonly HttpClient client = HttpClientFactory.Create(new CompressionMessageHandler());
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = SpecialJsonNamingPolicy.SnakeCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        IncludeFields = true,
        Converters = { new CompatApiCommitHashConverter(), },
    };

    //todo: cache results
    public async ValueTask<CompatResult?> GetCompatResultAsync(RequestBuilder requestBuilder, CancellationToken cancellationToken)
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
                    await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
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

    public async ValueTask<CompatResult?> GetCompatListSnapshotAsync(CancellationToken cancellationToken)
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
                    await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
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

    // https://github.com/AniLeo/rpcs3-compatibility/wiki/API:-Update
    public async ValueTask<UpdateInfo> GetUpdateAsync(CancellationToken cancellationToken, string? commit = null)
    {
        if (commit is not {Length: >6})
            commit = "somecommit";
        var result = new UpdateInfo();
        foreach (var arch in BuildArchList)
        {
            var tries = 3;
            do
            {
                try
                {
                    using var message = new HttpRequestMessage(
                        HttpMethod.Get,
                        $"https://update.rpcs3.net/?api=v3&os_arch={arch}&os_type=all&c={commit}"
                    );
                    using var response = await client
                        .SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                    try
                    {
                        var info = await response.Content
                            .ReadFromJsonAsync<UpdateCheckResult>(jsonOptions, cancellationToken)
                            .ConfigureAwait(false);
                        if (info is not null)
                            result[arch] = info;
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
        }
        return result;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        client.Dispose();
    }
}