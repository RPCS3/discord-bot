using System;
using System.Collections.Generic;
using System.Linq;

namespace CompatBot.Utils;

public static class TimeParser
{
    public static readonly Dictionary<string, TimeZoneInfo> TimeZoneMap;
        
    public static readonly Dictionary<string, string[]> TimeZoneAcronyms = new()
    {
        ["PT"] = new[] { "Pacific Standard Time", "America/Los_Angeles" },
        ["PST"] = new[] { "Pacific Standard Time", "America/Los_Angeles" },
        ["PDT"] = new[] { "Pacific Standard Time", "Pacific Daylight Time", "America/Los_Angeles" },
        ["EST"] = new[] { "Eastern Standard Time", "America/New_York" },
        ["EDT"] = new[] { "Eastern Standard Time", "Eastern Daylight Time", "America/New_York" },
        ["CEST"] = new[] { "Central European Standard Time", "Europe/Berlin" },
        ["BST"] = new[] { "British Summer Time", "GMT Standard Time", "Europe/London" },
        ["JST"] = new[] { "Japan Standard Time", "Tokyo Standard Time", "Asia/Tokyo" },
    };

    static TimeParser()
    {
        var uniqueNames = new HashSet<string>(
            from tznl in TimeZoneAcronyms.Values
            from tzn in tznl
            select tzn,
            StringComparer.InvariantCultureIgnoreCase
        );
        Config.Log.Trace("[TZI] Unique TZA names: " + uniqueNames.Count);
        var tzList = TimeZoneInfo.GetSystemTimeZones();
        Config.Log.Trace("[TZI] System TZI count: " + tzList.Count);
        var result = new Dictionary<string, TimeZoneInfo>();
        foreach (var tzi in tzList)
        {
            Config.Log.Trace($"[TZI] Checking {tzi.Id} ({tzi.DisplayName} / {tzi.StandardName} / {tzi.DaylightName})");
            if (uniqueNames.Contains(tzi.StandardName) || uniqueNames.Contains(tzi.DaylightName) || uniqueNames.Contains(tzi.Id))
            {
                Config.Log.Trace("[TZI] Looks like it's a match!");
                var acronyms = (
                    from tza in TimeZoneAcronyms
                    where tza.Value.Contains(tzi.StandardName) || tza.Value.Contains(tzi.DaylightName) || tza.Value.Contains(tzi.Id)
                    select tza.Key
                ).ToList();
                Config.Log.Trace("[TZI] Matched acronyms: " + string.Join(", ", acronyms));
                foreach (var tza in acronyms)
                    result[tza] = tzi;
            }
        }
        Config.Log.Trace("[TZI] Total matched acronyms: " + result.Count);
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