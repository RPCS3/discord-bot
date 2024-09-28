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

namespace IrdLibraryClient
{
    public class IrdClient
    {
        public static readonly string JsonUrl = "https://flexby420.github.io/playstation_3_ird_database/all.json";
        private readonly HttpClient client;
        private readonly JsonSerializerOptions jsonOptions;
        private static readonly string BaseDownloadUri = "https://github.com/FlexBy420/playstation_3_ird_database/raw/main/";

        public IrdClient()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                IncludeFields = true,
            };
        }

        public async Task<List<IrdInfo>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            query = query.ToUpper();
            try
            {
                using var response = await client.GetAsync(JsonUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    ApiConfig.Log.Error($"Failed to fetch IRD data: {response.StatusCode}");
                    return new List<IrdInfo>();
                }

                var jsonResult = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var irdData = JsonSerializer.Deserialize<Dictionary<string, List<IrdInfo>>>(jsonResult, jsonOptions);
                if (irdData == null)
                {
                    ApiConfig.Log.Error("Failed to deserialize IRD JSON data.");
                    return new List<IrdInfo>();
                }

                if (irdData.TryGetValue(query, out var items))
                {
                    return items;
                }

                return new List<IrdInfo>();
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
                return new List<IrdInfo>();
            }
        }

        public async Task<List<Ird>> DownloadAsync(string productCode, string localCachePath, CancellationToken cancellationToken)
        {
            var result = new List<Ird>();
            try
            {
                var searchResults = await SearchAsync(productCode, cancellationToken).ConfigureAwait(false);
                if (searchResults == null || !searchResults.Any())
                {
                    ApiConfig.Log.Debug($"No IRD files found for {productCode}");
                    return result;
                }

                foreach (var item in searchResults)
                {
                    var localFilePath = Path.Combine(localCachePath, $"{productCode}-{item.Link.Split('/').Last()}.ird");
                    if (!File.Exists(localFilePath))
                    {
                        try
                        {
                            var downloadLink = GetDownloadLink(item.Link);
                            var fileBytes = await client.GetByteArrayAsync(downloadLink, cancellationToken).ConfigureAwait(false);
                            await File.WriteAllBytesAsync(localFilePath, fileBytes, cancellationToken).ConfigureAwait(false);
                            result.Add(IrdParser.Parse(fileBytes));
                        }
                        catch (Exception ex)
                        {
                            ApiConfig.Log.Warn(ex, $"Failed to download {item.Link}: {ex.Message}");
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
        public static string GetDownloadLink(string relativeLink)
        {
            var fullUrl = new Uri(new Uri(BaseDownloadUri), relativeLink);
            return Uri.EscapeUriString(fullUrl.ToString());
        }
    }

    public class IrdInfo
    {
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;
    [JsonPropertyName("fw-ver")]
    public string? FwVer { get; set; }
    [JsonPropertyName("game-ver")]
    public string? GameVer { get; set; }
    [JsonPropertyName("app-ver")]
    public string? AppVer { get; set; }
    [JsonPropertyName("link")]
    public string Link { get; set; } = null!;
    }
}
