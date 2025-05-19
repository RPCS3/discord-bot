using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using CompatBot.EventHandlers;
using IrdLibraryClient;

namespace CompatBot.Database.Providers;

public static partial class DiscLanguageProvider
{
    [GeneratedRegex(@"^(?<title>.+?) \((?<region>\w+(, \w+)*)\)( \((?<lang>\w{2}(,\w{2})*)\))?", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex RedumpName();
    private static readonly Dictionary<string, List<string>> ProductCodeToVersionAndLangList = new();
    
    public static async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Config.Log.Info("Refreshing redump datfile…");
        var datXml = await IrdClient.GetRedumpDatfileAsync(Config.RedumpDatfileCachePath, cancellationToken).ConfigureAwait(false);
        if (datXml?.Root?.Descendants("game").ToList() is not { Count: > 0 } gameList)
            return;

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

            try
            {
                var langs = ParseLangList(name);
                foreach (var serial in serialList)
                {
                    if (!ProductCodeToVersionAndLangList.TryGetValue(serial, out var listOfLangs))
                        ProductCodeToVersionAndLangList[serial] = listOfLangs = [];
                    if (listOfLangs.Any(l => l.Equals(langs, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    listOfLangs.Add(langs);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to parse language list");
            }
        }
        Config.Log.Info("Completed refreshing redump datfile");
    }
    
    public static async ValueTask<IReadOnlyList<string>> GetLanguageListAsync(string productCode)
    {
        if (!ProductCodeToVersionAndLangList.TryGetValue(productCode, out var listOfLangs))
            return [];
        return listOfLangs.AsReadOnly();
    }

    private static string ParseLangList(string name)
    {
        string langs;
        if (RedumpName().Match(name) is not { Success: true } match)
            return "";
        else if (match.Groups["lang"].Value is { Length: > 0 } lang)
            langs = lang;
        else if (match.Groups["region"].Value is not { Length: > 0 } region)
            return "";
        else
        {
            var langList = region.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(MapRegionToLang)
                .Distinct()
                .Where(l => l is { Length: > 0 })
                //.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
            langs = string.Join(",", langList);
        }
        var langsParts = langs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Select(MapLangToFlag)
            //.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return string.Join(' ', langsParts);
    }

    private static string MapRegionToLang(string region)
#if DEBUG
        => RegionToLang.TryGetValue(region, out var result)
            ? result
            : throw new InvalidDataException($"No mapping from region {region} to language");
#else
        => RegionToLang.GetValueOrDefault(region, "");
 #endif

    private static string MapLangToFlag(string lang)
#if DEBUG
        => LangToFlag.TryGetValue(lang, out var result)
            ? result
            : throw new InvalidDataException($"No mapping from language {lang} to flag");
#else
        => LangToFlag.GetValueOrDefault(lang, "🏁");
 #endif

    private static readonly Dictionary<string, string> RegionToLang = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia"] = "Ja",
        ["Australia"] = "En",
        ["Austria"] = "De",
        ["Brazil"] = "Pt",
        ["Canada"] = "En,Fr",
        ["Europe"] = "En-UK",
        ["France"] = "Fr",
        ["Germany"] = "De",
        ["Greece"] = "El",
        ["India"] = "En",
        ["Italy"] = "It",
        ["Japan"] = "Ja",
        ["Korea"] = "Ko",
        ["Mexico"] = "Es-MX",
        ["Poland"] = "Pl",
        ["Russia"] = "Ru",
        ["Spain"] = "Es",
        ["Switzerland"] = "De",
        ["Turkey"] = "Tr",
        ["UK"] = "En-UK",
        ["USA"] = "En",
    };

    private static readonly Dictionary<string, string> LangToFlag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["af"] = "🇿🇦",
        ["ar"] = "🇸🇦",
        ["bg"] = "🇧🇬",
        ["ca"] = "🇦🇩",
        ["cs"] = "🇨🇿",
        ["da"] = "🇩🇰",
        ["de"] ="🇩🇪",
        ["el"] = "🇬🇷",
        ["en"] = "🇺🇸",
        ["en-UK"] = "🇬🇧",
        ["es"] = "🇪🇸",
        ["es-MX"] = "🇲🇽",
        ["eu"] = "🏴󠁥󠁳󠁰󠁶󠁿",
        ["fi"] = "🇫🇮",
        ["fr"] = "🇫🇷",
        ["gd"] = "🏴󠁧󠁢󠁳󠁣󠁴󠁿",
        ["hr"] = "🇭🇷",
        ["hu"] = "🇭🇺",
        ["it"] = "🇮🇹",
        ["ja"] = "🇯🇵",
        ["ko"] = "🇰🇷",
        ["nl"] = "🇳🇱",
        ["no"] = "🇳🇴",
        ["pl"] = "🇵🇱",
        ["pt"] = "🇧🇷",
        ["ro"] = "🇷🇴",
        ["ru"] = "🇷🇺",
        ["sk"] = "🇸🇰",
        ["sv"] = "🇸🇪",
        ["tr"] = "🇹🇷",
        ["zh"] = "🇨🇳",
    };
}