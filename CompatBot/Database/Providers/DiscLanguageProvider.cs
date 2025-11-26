using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using CompatBot.EventHandlers;
using IrdLibraryClient;

namespace CompatBot.Database.Providers;

public static partial class DiscLanguageProvider
{
    [GeneratedRegex(@"^(?<title>.+?) \((?<region>(\w|\s)+(, (\w|\s)+)*)\)( \((?<lang>\w{2}(,\w{2})*)\))?", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex RedumpName();
    private static readonly Dictionary<string, List<string>> ProductCodeToVersionAndLangList = new();
    
    public static async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Config.Log.Info("Refreshing redump datfile…");
        var datXml = await IrdClient.GetRedumpDatfileAsync(Config.RedumpDatfileCachePath, cancellationToken).ConfigureAwait(false);
        if (datXml?.Root?.Descendants("game").ToList() is not { Count: > 0 } gameList)
            return;
#if DEBUG
        var longestLangList = "";
        var gameWithLongestLangList = "";
#endif
        foreach (var gameInfo in gameList)
        {
            var name = (string?)gameInfo.Attribute("name");
            var serialList = ((string?)gameInfo.Element("serial"))?
                .Replace(" ", "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(ProductCodeLookup.GetProductIds)
                .Distinct()
                .ToList();
#if DEBUG
            var desc = (string?)gameInfo.Element("description");
            if (name != desc)
                throw new InvalidDataException("Unexpected datfile format discrepancy");
#endif
            if (serialList is not { Count: > 0 } || name is not {Length: >0})
                continue;

            foreach (var serial in serialList)
                try
                {
                    var langs = ParseLangList(serial[2], name);
                    if (langs is not { Length: > 0 })
                        continue;
#if DEBUG
                    if (langs.Length > longestLangList.Length)
                    {
                        longestLangList = langs;
                        gameWithLongestLangList = serialList[0];
                    }
#endif
                    if (!ProductCodeToVersionAndLangList.TryGetValue(serial, out var listOfLangs))
                        ProductCodeToVersionAndLangList[serial] = listOfLangs = [];
                    if (listOfLangs.Any(l => l.Equals(langs, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    listOfLangs.Add(langs);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to parse language list");
                }
        }
#if DEBUG
        Config.Log.Debug($"Game product code with the longest language list: {gameWithLongestLangList} ({longestLangList})");
#endif
        Config.Log.Info("Completed refreshing redump datfile");
    }
    
    public static async ValueTask<IReadOnlyList<string>> GetLanguageListAsync(string productCode)
    {
        if (!ProductCodeToVersionAndLangList.TryGetValue(productCode, out var listOfLangs))
            return [];
        return listOfLangs.AsReadOnly();
    }

    private static string ParseLangList(char productCodeRegion, string name)
    {
        if (RedumpName().Match(name) is not { Success: true } match)
            return "";

        string langs = "";
        List<string> flagList = [];
        if (match.Groups["lang"].Value is { Length: > 0 } lang)
            langs = lang;
        else if (match.Groups["region"].Value is not { Length: > 0 } region)
            return "";
        else
        {
            flagList = region.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(MapRegionToFlag)
                .Distinct()
                .Where(l => l is { Length: > 0 })
                //.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        flagList = flagList.Concat(
            langs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => MapLangToFlag(productCodeRegion, l))
        ).Distinct()
        //.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
        .ToList();
        return string.Join(' ', flagList);
    }

    private static string MapRegionToFlag(string region)
        => RegionToFlag.TryGetValue(region, out var result)
            ? result
#if DEBUG
            : throw new InvalidDataException($"No mapping from region {region} to language");
#else
            : "";
 #endif

    private static string MapLangToFlag(char region, string lang)
        => region switch
           {
               'E' => EuLangToFlag.TryGetValue(lang, out var flag) ? flag : null,
               'U' => UsLangToFlag.TryGetValue(lang, out var flag) ? flag : null,
               _ => null
           }
           ?? (
               LangToFlag.TryGetValue(lang, out var result)
                   ? result
#if DEBUG
                   : throw new InvalidDataException($"No mapping from language {lang} to flag")
#else
                   : "🏁"
#endif
           );

    private static readonly Dictionary<string, string> RegionToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia"] = "🇯🇵",
        ["Australia"] = "🇦🇺",
        ["Austria"] = "🇦🇹",
        ["Brazil"] = "🇧🇷",
        ["Canada"] = "🇨🇦",
        ["Europe"] = "🇪🇺",
        ["France"] = "🇫🇷",
        ["Germany"] = "🇩🇪",
        ["Greece"] = "🇬🇷",
        ["India"] = "🇮🇳",
        ["Italy"] = "🇮🇹",
        ["Japan"] = "🇯🇵",
        ["Korea"] = "🇰🇷",
        ["Latin America"] = "🇱🇽", // not a real code
        ["Mexico"] = "🇲🇽",
        ["New Zealand"] = "🇳🇿",
        ["Poland"] = "🇵🇱",
        ["Russia"] = "🇷🇺",
        ["Spain"] = "🇪🇸",
        ["Switzerland"] = "🇨🇭",
        ["Turkey"] = "🇹🇷",
        ["UK"] = "🇬🇧",
        ["United Arab Emirates"] = "🇸🇦",
        ["USA"] = "🇺🇸",
    };

    // ISO-639 language code to ISO-3166-2 country code
    private static readonly Dictionary<string, string> LangToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["af"] = "🇿🇦", // Afrikaans - South Africa
        ["ar"] = "🇸🇦", // Arabic - Saudi Arabia
        ["bg"] = "🇧🇬", // Bulgarian
        ["ca"] = "🇦🇩", // Catalan - Andorra
        ["cs"] = "🇨🇿",
        ["da"] = "🇩🇰", // Danish - Denmark
        ["de"] = "🇩🇪",
        ["de-AT"] = "🇦🇹",
        ["el"] = "🇬🇷", // Greek
        ["en"] = "🇺🇸",
        ["en-UK"] = "🇬🇧",
        ["es"] = "🇪🇸",
        ["es-MX"] = "🇲🇽",
        ["eu"] = "🏴󠁥󠁳󠁰󠁶󠁿", // Basque - France / Spain
        ["fi"] = "🇫🇮",
        ["fr"] = "🇫🇷",
        ["gd"] = "🏴󠁧󠁢󠁳󠁣󠁴󠁿", // Gaelic - Scotland
        ["hr"] = "🇭🇷", // Croatian
        ["hu"] = "🇭🇺",
        ["it"] = "🇮🇹",
        ["ja"] = "🇯🇵",
        ["ko"] = "🇰🇷",
        ["nl"] = "🇳🇱",
        ["no"] = "🇳🇴",
        ["pl"] = "🇵🇱",
        ["pt"] = "🇵🇹",
        ["pt-BR"] = "🇧🇷",
        ["ro"] = "🇷🇴",
        ["ru"] = "🇷🇺",
        ["sk"] = "🇸🇰", // Slovak
        ["sv"] = "🇸🇪", // Swedish
        ["tr"] = "🇹🇷",
        ["zh"] = "🇨🇳",
    };

    private static readonly Dictionary<string, string> UsLangToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "🇺🇸",
        // ["es"] = "🇲🇽",
        // ["pt"] = "🇧🇷",
    };

    private static readonly Dictionary<string, string> EuLangToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "🇬🇧",
    };
}