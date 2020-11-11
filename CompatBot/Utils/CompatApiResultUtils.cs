using System;
using System.Collections.Generic;
using System.Linq;
using CompatApiClient.POCOs;

namespace CompatBot.Utils
{
    internal static class CompatApiResultUtils
    {
        public static List<(string code, TitleInfo info, double score)> GetSortedList(this CompatResult result)
        {
            var search = result.RequestBuilder.Search;
            if (string.IsNullOrEmpty(search) || !result.Results.Any())
                return result.Results
                    .OrderBy(kvp => kvp.Value.Title)
                    .ThenBy(kvp => kvp.Key)
                    .Select(kvp => (kvp.Key, kvp.Value, 0.0))
                    .ToList();

            var sortedList = result.Results
                .Select(kvp => (code: kvp.Key, info: kvp.Value, score: GetScore(search, kvp.Value)))
                .OrderByDescending(t => t.score)
                .ThenBy(t => t.info.Title)
                .ThenBy(t => t.code)
                .ToList();
            if (sortedList.First().score < 0.2)
                sortedList = sortedList
                    .OrderBy(kvp => kvp.info.Title)
                    .ThenBy(kvp => kvp.code)
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
