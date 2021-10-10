using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using Microsoft.IO;
using NLog;

namespace CompatApiClient
{
    using ReturnCodeType = Dictionary<int, (bool displayResults, bool overrideAll, bool displayFooter, string info)>;

    public static class ApiConfig
    {
        public static readonly string ProductName = "RPCS3CompatibilityBot";
        public static readonly string ProductVersion = "2.0";
        public static readonly ProductInfoHeaderValue ProductInfoHeader = new(ProductName, ProductVersion);
        public static int Version { get; } = 1;
        public static Uri BaseUrl { get; } = new("https://rpcs3.net/compatibility");
        public static string DateInputFormat { get; } = "yyyy-M-d";
        public static string DateOutputFormat { get; } = "yyy-MM-dd";
        public static string DateQueryFormat { get; } = "yyyyMMdd";

        public static readonly ReturnCodeType ReturnCodes = new()
        {
            {0, (true, false, true, "Results successfully retrieved.")},
            {1, (false, false, true, "No results.") },
            {2, (true, false, true, "No match was found, displaying results for: ***{0}***.") },
            {-1, (false, true, false, "{0}: Internal error occurred, please contact Ani and Nicba1010") },
            {-2, (false, true, false, "{0}: API is undergoing maintenance, please try again later.") },
            {-3, (false, false, false, "Illegal characters found, please try again with a different search term.") },
        };

        public static readonly List<int> ResultAmount = new(){25, 50, 100, 200};

        public static readonly Dictionary<char, string[]> Directions = new()
        {
            {'a', new []{"a", "asc", "ascending"}},
            {'d', new []{"d", "desc", "descending"} },
        };

        public static readonly Dictionary<string, int> Statuses = new()
        {
            {"all", 0 },
            {"playable", 1 },
            {"ingame", 2 },
            {"intro", 3 },
            {"loadable", 4 },
            {"nothing", 5 },
        };

        public static readonly Dictionary<string, int> SortTypes = new()
        {
            {"id", 1 },
            {"title", 2 },
            {"status", 3 },
            {"date", 4 },
        };

        public static readonly Dictionary<char, string[]> ReleaseTypes = new()
        {
            {'b', new[] {"b", "d", "disc", "disk", "bluray", "blu-ray"}},
            {'n', new[] {"n", "p", "PSN"}},
        };

        public static readonly Dictionary<string, char> ReverseDirections;
        public static readonly Dictionary<string, char> ReverseReleaseTypes;

        private static Dictionary<TV, TK> Reverse<TK, TV>(this Dictionary<TK, TV[]> dic, IEqualityComparer<TV> comparer)
            where TK: notnull
            where TV: notnull
        {
            return (
                from kvp in dic
                from val in kvp.Value
                select (val, kvp.Key)
            ).ToDictionary(rkvp => rkvp.val, rkvp => rkvp.Key, comparer);
        }

        public static readonly ILogger Log;
        public static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

        static ApiConfig()
        {
            Log = LogManager.GetLogger("default");
            try
            {
                ReverseDirections = Directions.Reverse(StringComparer.InvariantCultureIgnoreCase);
                ReverseReleaseTypes = ReleaseTypes.Reverse(StringComparer.InvariantCultureIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Fatal(e);
                ReverseDirections = new Dictionary<string, char>();
                ReverseReleaseTypes = new Dictionary<string, char>();
            }
        }
    }
}