using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.Formatters;
using CompatApiClient.Utils;
using IrdLibraryClient.IrdFormat;
using IrdLibraryClient.POCOs;

namespace IrdLibraryClient
{
    public class IrdClient
    {
        public static readonly string BaseUrl = "http://jonnysp.bplaced.net";

        private readonly HttpClient client;
        private readonly JsonSerializerOptions jsonOptions;
        private static readonly Regex IrdFilename = new(@"ird/(?<filename>\w{4}\d{5}-[A-F0-9]+\.ird)", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);

        public IrdClient()
        {
            client = HttpClientFactory.Create(new CompressionMessageHandler());
            jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = SpecialJsonNamingPolicy.SnakeCase,
                IgnoreNullValues = true,
                IncludeFields = true,
            };
        }

        public static string GetDownloadLink(string irdFilename) => $"{BaseUrl}/ird/{irdFilename}";
        public static string GetInfoLink(string irdFilename) => $"{BaseUrl}/info.php?file=ird/{irdFilename}";

        public async Task<SearchResult?> SearchAsync(string query, CancellationToken cancellationToken)
        {
            try
            {
                var requestUri = new Uri(BaseUrl + "/data.php")
                    .SetQueryParameters(
                        ("draw", query.Length.ToString()),

                        ("columns[0][data]", "id"),
                        ("columns[0][name]", ""),
                        ("columns[0][searchable]", "true"),
                        ("columns[0][orderable]", "true"),
                        ("columns[0][search][value]", ""),
                        ("columns[0][search][regex]", "false"),

                        ("columns[1][data]", "title"),
                        ("columns[1][name]", ""),
                        ("columns[1][searchable]", "true"),
                        ("columns[1][orderable]", "true"),
                        ("columns[1][search][value]", ""),
                        ("columns[1][search][regex]", "false"),

                        ("order[0][column]", "0"),
                        ("order[0][dir]", "asc"),

                        ("start", "0"),
                        ("length", "10"),

                        ("search[value]", query.Trim(100)),

                        ("_", DateTime.UtcNow.Ticks.ToString())
                    );
                using var getMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
                using var response = await client.SendAsync(getMessage, cancellationToken).ConfigureAwait(false);
                try
                {
                    await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                    var result = await response.Content.ReadFromJsonAsync<SearchResult>(jsonOptions, cancellationToken).ConfigureAwait(false);
                    if (result?.Data?.Count > 0)
                        foreach (var item in result.Data)
                        {
                            item.Filename = GetIrdFilename(item.Filename);
                            item.Title = GetTitle(item.Title);
                        }
                    return result;
                }
                catch (Exception e)
                {
                    ConsoleLogger.PrintError(e, response);
                    return null;
                }
            }
            catch (Exception e)
            {
                ApiConfig.Log.Error(e);
                return null;
            }
        }

        public async Task<List<Ird>> DownloadAsync(string productCode, string localCachePath, CancellationToken cancellationToken)
        {
            var result = new List<Ird>();
            try
            {
                // first we search local cache and try to load whatever data we can
                var localCacheItems = new List<string>();
                try
                {
                    var tmpCacheItemList = Directory.GetFiles(localCachePath, productCode + "*.ird", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .ToList();
                    foreach (var item in tmpCacheItemList)
                    {
                        if (string.IsNullOrEmpty(item))
                            continue;
                        
                        try
                        {
                            result.Add(IrdParser.Parse(await File.ReadAllBytesAsync(Path.Combine(localCachePath, item), cancellationToken).ConfigureAwait(false)));
                            localCacheItems.Add(item);
                        }
                        catch (Exception ex)
                        {
                            ApiConfig.Log.Warn(ex, "Error reading local IRD file: " + ex.Message);
                        }
                    }
                }
                catch (Exception e)
                {
                    ApiConfig.Log.Warn(e, "Error accessing local IRD cache: " + e.Message);
                }
                ApiConfig.Log.Debug($"Found {localCacheItems.Count} cached items for {productCode}");
                SearchResult? searchResult = null;

                // then try to do IRD Library search
                try
                {
                    searchResult = await SearchAsync(productCode, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ApiConfig.Log.Error(e);
                }
                var tmpFilesToGet = searchResult?.Data?
                    .Select(i => i.Filename)
                    .Except(localCacheItems, StringComparer.InvariantCultureIgnoreCase)
                    .ToList();
                if (tmpFilesToGet is null or {Count: 0})
                    return result;

                // as IRD Library could return more data than we found, try to check for all the items locally
                var filesToDownload = new List<string>();
                foreach (var item in tmpFilesToGet)
                {
                    if (string.IsNullOrEmpty(item))
                        continue;
                    
                    try
                    {
                        var localItemPath = Path.Combine(localCachePath, item);
                        if (File.Exists(localItemPath))
                        {
                            result.Add(IrdParser.Parse(await File.ReadAllBytesAsync(localItemPath, cancellationToken).ConfigureAwait(false)));
                            localCacheItems.Add(item);
                        }
                        else
                            filesToDownload.Add(item);
                    }
                    catch (Exception ex)
                    {
                        ApiConfig.Log.Warn(ex, "Error reading local IRD file: " + ex.Message);
                        filesToDownload.Add(item);
                    }
                }
                ApiConfig.Log.Debug($"Found {tmpFilesToGet.Count} total matches for {productCode}, {result.Count} already cached");
                if (filesToDownload.Count == 0)
                    return result;

                // download the remaining .ird files
                foreach (var item in filesToDownload)
                {
                        try
                        {
                            var resultBytes = await client.GetByteArrayAsync(GetDownloadLink(item), cancellationToken).ConfigureAwait(false);
                            result.Add(IrdParser.Parse(resultBytes));
                            try
                            {
                                await File.WriteAllBytesAsync(Path.Combine(localCachePath, item), resultBytes, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                ApiConfig.Log.Warn(ex, $"Failed to write {item} to local cache: {ex.Message}");
                            }
                        }
                        catch (Exception e)
                        {
                            ApiConfig.Log.Warn(e, $"Failed to download {item}: {e.Message}");
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

        private static string? GetIrdFilename(string? html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var matches = IrdFilename.Matches(html);
            if (matches.Count > 0)
                return matches[0].Groups["filename"].Value;
            
            ApiConfig.Log.Warn("Couldn't parse IRD filename from " + html);
            return null;

        }

        private static string? GetTitle(string? html)
        {
            if (string.IsNullOrEmpty(html))
                return null;

            var idx = html.LastIndexOf("</span>", StringComparison.Ordinal);
            var result = html[(idx + 7)..].Trim();
            if (string.IsNullOrEmpty(result))
                return null;

            return result;
        }
   }
}
