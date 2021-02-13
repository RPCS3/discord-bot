using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using PsnClient.POCOs;

namespace CompatBot.Utils
{
    public static class PsnMetaExtensions
    {
        private static readonly MemoryCache ParsedData = new(new MemoryCacheOptions {ExpirationScanFrequency = TimeSpan.FromHours(1)});
        private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);

        internal static List<(string resolution, string aspectRatio)> GetSupportedResolutions(this TitleMeta meta)
            => GetSupportedResolutions(meta.Resolution);

        internal static List<(string resolution, string aspectRatio)> GetSupportedResolutions(string resolutionList)
        {
            if (ParsedData.TryGetValue(resolutionList, out List<(string, string)> result))
                return result;

            var resList = resolutionList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Convert)
                .ToList();
            ParsedData.Set(resolutionList, resList, CacheDuration);
            return resList;
        }

        private static (string resolution, string aspectRatio) Convert(string verticalRes)
            => verticalRes.ToUpper() switch
            {
                "480SQ" => ("720x480", "4:3"),
                "576SQ" => ("720x576", "4:3"),

                "480" => ("720x480", "16:9"),
                "576" => ("720x576", "16:9"),
                "720" => ("1280x720", "16:9"),
                "1080" => ("1920x1080", "16:9"),
#if DEBUG
                _ => throw new InvalidDataException($"Unknown resolution {verticalRes} in PSN meta data"),
#else
                _ => (verticalRes, "16:9"),
#endif
            };
    }
}