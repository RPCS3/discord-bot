using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using IrdLibraryClient.IrdFormat;
using IrdLibraryClient.POCOs;
using Microsoft.Extensions.Caching.Memory;

namespace IrdLibraryClient
{
    public class IrdClient
    {
        private static readonly Uri BaseDownloadUri = new("https://github.com/FlexBy420/playstation_3_ird_database/raw/main/");
        private static readonly HttpClient Client = HttpClientFactory.Create(new CompressionMessageHandler());
        private static readonly JsonSerializerOptions JsonOptions= new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
        };
        private static readonly MemoryCache JsonCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromHours(1) });

        public static readonly Uri JsonUrl = new("https://flexby420.github.io/playstation_3_ird_database/all.json");

        public async Task<List<IrdInfo>> SearchAsync(string query, CancellationToken cancellationToken)
        {            
            List<IrdInfo> result = [];
            if (!JsonCache.TryGetValue("json", out Dictionary<string, List<IrdInfo>>? irdData)
                || irdData is not { Count: > 0 })
            {
                try
                {
                    using var response = await Client.GetAsync(JsonUrl, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        ApiConfig.Log.Error($"Failed to fetch IRD data: {response.StatusCode}");
                        return result;
                    }

                    var jsonResult = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    irdData = JsonSerializer.Deserialize<Dictionary<string, List<IrdInfo>>>(jsonResult, JsonOptions);
                    JsonCache.Set("json", irdData, TimeSpan.FromHours(4));
                }
                catch (Exception e)
                {
                    ApiConfig.Log.Error(e, "Failed to fetch IRD data.");
                    return result;
                }
            }

            if (irdData is null)
            {
                ApiConfig.Log.Error("Failed to deserialize IRD JSON data.");
                return result;
            }

            if (irdData.TryGetValue(query.ToUpperInvariant(), out var items))
                result.AddRange(items);
            result.AddRange(
                from lst in irdData.Values
                from irdInfo in lst
                where irdInfo.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false
                select irdInfo 
            );
            return result;
        }

        public async Task<List<Ird>> DownloadAsync(string productCode, string localCachePath, CancellationToken cancellationToken)
        {
            var result = new List<Ird>();
            try
            {
                var searchResults = await SearchAsync(productCode, cancellationToken).ConfigureAwait(false);
                if (searchResults is not {Count: >0})
                {
                    ApiConfig.Log.Debug($"No IRD files found for {productCode}");
                    return result;
                }

                foreach (var item in searchResults)
                {
                    var localFilePath = Path.Combine(localCachePath, $"{productCode}-{item.Link.Split('/').Last()}.ird");
                    if (File.Exists(localFilePath))
                    {
                        var irdData = await File.ReadAllBytesAsync(localFilePath, cancellationToken).ConfigureAwait(false);
                        try
                        {
                            result.Add(IrdParser.Parse(irdData));
                        }
                        catch (Exception e)
                        {
                            ApiConfig.Log.Warn(e, $"Failed to parse locally cached IRD file {localFilePath}");
                            try { File.Delete(localFilePath); } catch {}
                        }
                    }
                    else
                    {
                        try
                        {
                            var downloadLink = GetDownloadLink(item.Link);
                            var fileBytes = await Client.GetByteArrayAsync(downloadLink, cancellationToken).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(localFilePath, fileBytes, cancellationToken).ConfigureAwait(false);
                            result.Add(IrdParser.Parse(fileBytes));
                        }
                        catch (Exception e)
                        {
                            ApiConfig.Log.Warn(e, $"Failed to download {item.Link}: {e.Message}");
                        }
                    }
                }
                ApiConfig.Log.Debug($"Returning {result.Count} .ird files for {productCode}");
                return result;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
                return result;
            }
        }
        
        public static string GetDownloadLink(string relativeLink) => Uri.EscapeUriString(new Uri(BaseDownloadUri, relativeLink).ToString());
    }
}
