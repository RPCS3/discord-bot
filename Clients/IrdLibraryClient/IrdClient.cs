using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
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
        private static readonly Uri RedumpDatDownloadUri = new("http://redump.org/datfile/ps3/serial,version");
        private static readonly HttpClient Client = HttpClientFactory.Create(new CompressionMessageHandler());
        private static readonly JsonSerializerOptions JsonOptions= new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
        };
        private static readonly MemoryCache JsonCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromHours(1) });

        public static readonly Uri JsonUrl = new("https://flexby420.github.io/playstation_3_ird_database/all.json");

        public static async ValueTask<List<IrdInfo>> SearchAsync(string query, CancellationToken cancellationToken)
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

        public async ValueTask<List<Ird>> DownloadAsync(string productCode, string localCachePath, CancellationToken cancellationToken)
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
                    var localFilePath = Path.Combine(localCachePath, $"{productCode}-{item.Link.Split('/').Last()}");
                    if (!localCachePath.EndsWith(".ird", StringComparison.OrdinalIgnoreCase))
                        localFilePath += ".ird";
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

        public static async ValueTask<XDocument?> GetRedumpDatfileAsync(string localCachePath, CancellationToken cancellationToken)
        {
            try
            {
                string? localZipFilePath;
                if (Directory.Exists(localCachePath)
                    && Directory.EnumerateFiles(
                        localCachePath,
                        "*Datfile*.zip",
                        new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }
                    ).OrderBy(n => n).LastOrDefault() is string localFilePath)
                {
                    if (new FileInfo(localFilePath).CreationTimeUtc.AddDays(7) > DateTime.UtcNow)
                        localZipFilePath = localFilePath;
                    else
                        localZipFilePath = await DownloadLatestRedumpDatfileAsync(localCachePath, cancellationToken).ConfigureAwait(false)
                                           ?? localFilePath;
                }
                else
                    localZipFilePath = await DownloadLatestRedumpDatfileAsync(localCachePath, cancellationToken).ConfigureAwait(false);
                if (localZipFilePath is not { Length: > 0 })
                    return null;

                await using var zipStream = File.Open(localZipFilePath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
                using var zipFile = new ZipArchive(zipStream, ZipArchiveMode.Read, true);
                var fileEntry = zipFile.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
                if (fileEntry is null)
                {
                    ApiConfig.Log.Warn($"No datfile inside {localZipFilePath}");
                    return null;
                }

                await using var xmlStream = fileEntry.Open();
                using var xmlReader = XmlReader.Create(xmlStream, new(){ Async = true, DtdProcessing = DtdProcessing.Ignore });
                if (await XDocument.LoadAsync(xmlReader, LoadOptions.None, cancellationToken).ConfigureAwait(false) is not XDocument doc)
                {
                    ApiConfig.Log.Warn($"Failed to deserialize {fileEntry.Name} from {localZipFilePath}");
                    return null;
                }

                return doc;
            }
            catch (Exception e)
            {
                ApiConfig.Log.Warn(e, "Failed to get redump datfile content.");
            }
            return null;
        }

        private static async ValueTask<string?> DownloadLatestRedumpDatfileAsync(string localCachePath, CancellationToken cancellationToken)
        {
            string? localFilePath = null;
            using var request = new HttpRequestMessage(HttpMethod.Get, RedumpDatDownloadUri);
            var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode &&
                response.Content.Headers.ContentDisposition?.FileName is string filename)
            {
                if (filename.StartsWith('"') && filename.EndsWith('"'))
                    filename = filename[1..^1];
                ApiConfig.Log.Info($"Latest redump datfile snapshot: {filename}");
                var localCacheFilename = Path.Combine(localCachePath, filename);
                if (File.Exists(localCacheFilename))
                {
                    ApiConfig.Log.Info("Using local copy of redump datfile snapshot");
                    localFilePath = localCacheFilename;
                }
                else
                {
                    var resultBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (!Directory.Exists(localCachePath))
                            Directory.CreateDirectory(localCachePath);
                        ApiConfig.Log.Info($"Saving latest redump datfile snapshot in local cache: {filename}...");
                        await File.WriteAllBytesAsync(localCacheFilename, resultBytes, cancellationToken);
                        localFilePath = localCacheFilename;
                    }
                    catch (Exception ex)
                    {
                        ApiConfig.Log.Warn(ex, $"Failed to write {filename} to local cache: {ex.Message}");
                    }
                }
            }
            return localFilePath;
        }
        
        private static string EscapeSegments(string relativePath)
        {
            var segments = relativePath.Split('/');
            for (var i = 0; i < segments.Length; i++)
                segments[i] = Uri.EscapeDataString(segments[i]);
            return string.Join("/", segments);
        }
        
        public static Uri GetDownloadLink(string relativeLink) => new(BaseDownloadUri, EscapeSegments(relativeLink));
        public static string GetEscapedDownloadLink(string relativeLink) => GetDownloadLink(relativeLink).AbsoluteUri;
    }
}
