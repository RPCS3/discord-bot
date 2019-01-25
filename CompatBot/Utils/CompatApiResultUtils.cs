using System;
using System.Collections.Generic;
using System.Linq;
using CompatApiClient.POCOs;

namespace CompatBot.Utils
{
    internal static class CompatApiResultUtils
    {
        public static List<KeyValuePair<string, TitleInfo>> GetSortedList(this CompatResult result)
        {
            var search = result.RequestBuilder.search;
            var sortedList = result.Results.ToList();
            if (!string.IsNullOrEmpty(search))
                sortedList = sortedList
                    .OrderByDescending(kvp => GetScore(search, kvp.Value))
                    .ThenBy(kvp => kvp.Value.Title)
                    .ThenBy(kvp => kvp.Key)
                    .ToList();
            if (GetScore(search, sortedList.First().Value) < 0.2)
                sortedList = sortedList
                    .OrderBy(kvp => kvp.Value.Title)
                    .ThenBy(kvp => kvp.Key)
                    .ToList();
            return sortedList;
        }

        public static double GetScore(string search, TitleInfo titleInfo)
        {
            var score = Math.Max(
                search.GetFuzzyCoefficientCached(titleInfo.Title),
                search.GetFuzzyCoefficientCached(titleInfo.AlternativeTitle)
            );
            if (score > 0.3)
                return score;
            return 0;
        }
    }
}
