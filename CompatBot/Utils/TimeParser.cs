using System;
using System.Collections.Generic;
using System.Linq;

namespace CompatBot.Utils
{
    public static class TimeParser
    {
        public static readonly Dictionary<string, TimeZoneInfo> TimeZoneMap;
        
        public static readonly Dictionary<string, string[]> TimeZoneAcronyms = new Dictionary<string, string[]>
        {
            ["PT"] = new[] { "Pacific Standard Time" },
            ["PST"] = new[] { "Pacific Standard Time" },
            ["PDT"] = new[] { "Pacific Standard Time" },
            ["EST"] = new[] { "Eastern Standard Time" },
            ["EDT"] = new[] { "Eastern Standard Time" },
            ["CEST"] = new[] { "Central European Standard Time" },
            ["BST"] = new[] { "British Summer Time", "GMT Standard Time" },
            ["JST"] = new[] { "Japan Standard Time", "Tokyo Standard Time" },
        };

        static TimeParser()
        {
            var uniqueNames = new HashSet<string>(
                from tznl in TimeZoneAcronyms.Values
                from tzn in tznl
                select tzn,
                StringComparer.InvariantCultureIgnoreCase
            );
            Config.Log.Debug("[TZI] Unique TZA names: " + uniqueNames.Count);
            var tzList = TimeZoneInfo.GetSystemTimeZones();
            Config.Log.Debug("[TZI] System TZI count: " + tzList.Count);
            var result = new Dictionary<string, TimeZoneInfo>();
            foreach (var tzi in tzList)
            {
                Config.Log.Debug($"[TZI] Checking {tzi.Id} ({tzi.StandardName} / {tzi.DaylightName})");
                if (uniqueNames.Contains(tzi.StandardName) || uniqueNames.Contains(tzi.DaylightName))
                {
                    Config.Log.Debug("[TZI] Looks like it's a match!");
                    var acronyms = from tza in TimeZoneAcronyms
                        where tza.Value.Any(name => name.Equals(tzi.StandardName, StringComparison.InvariantCultureIgnoreCase))
                              || tza.Value.Any(name => name.Equals(tzi.DaylightName, StringComparison.InvariantCulture))
                        select tza.Key;
                    Config.Log.Debug("[TZI] Matched acronyms: " + string.Join(", ", acronyms));
                    foreach (var tza in acronyms)
                        result[tza] = tzi;
                }
            }
            Config.Log.Debug("[TZI] Total matched acronyms: " + result.Count);
            TimeZoneMap = result;
        }

        public static bool TryParse(string dateTime, out DateTime result)
        {
            result = default;
            if (string.IsNullOrEmpty(dateTime))
                return false;

            dateTime = dateTime.ToUpperInvariant();
            if (char.IsDigit(dateTime[^1]))
                return DateTime.TryParse(dateTime, out result);

            var cutIdx = dateTime.LastIndexOf(' ');
            if (cutIdx < 0)
                return false;

            var tza = dateTime[(cutIdx+1)..];
            dateTime = dateTime[..cutIdx];
            if (TimeZoneMap.TryGetValue(tza, out var tzi))
            {
                if (!DateTime.TryParse(dateTime, out result))
                    return false;

                result = TimeZoneInfo.ConvertTimeToUtc(result, tzi);
                return true;
            }

            return false;
        }


        public static DateTime Normalize(this DateTime date) =>
            date.Kind switch
            {
                DateTimeKind.Utc => date,
                DateTimeKind.Local => date.ToUniversalTime(),
                _ => date.AsUtc(),
            };

        public static List<string> GetSupportedTimeZoneAbbreviations() => TimeZoneMap.Keys.ToList();
    }
}
