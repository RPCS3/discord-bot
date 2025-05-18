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
        var datXml = await IrdClient.GetRedumpDatfileAsync(Config.RedumpDatfileCachePath, cancellationToken).ConfigureAwait(false);
        if (datXml?.Root?.Descendants("game").ToList() is not { Count: > 0 } gameList)
            return;

        foreach (var gameInfo in gameList)
        {
            var name = (string?)gameInfo.Attribute("name");
            var version = (string?)gameInfo.Element("version") ?? "01.00";
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
    }
    
    public static async ValueTask<IReadOnlyList<string>> GetLanguageListAsync(string productCode)
    {
        if (!ProductCodeToVersionAndLangList.TryGetValue(productCode, out var listOfLangs))
            return [];
        return listOfLangs.AsReadOnly();
    }

    private static string ParseLangList(string name)
    {
        if (RedumpName().Match(name) is not { Success: true } match)
            return "";

        if (match.Groups["lang"].Value is { Length: > 0 } lang)
        {
            var langs = lang.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return string.Join(",", langs);
        }

        if (match.Groups["region"].Value is not { Length: > 0 } region)
            return "";

        var langList = region.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MapRegionToLang)
            .Distinct()
            .Where(l => l is { Length: > 0 })
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return string.Join(",", langList);
    }

    private static string MapRegionToLang(string region)
        => region switch
        {
            "Japan" => "Ja",
            "Asia" => "Ja",
            "USA" => "En",
            "Europe" => "En",
            "UK" => "En",
            "Australia" => "En",
            "Canada" => "En",
            "India" => "En",
            "Korea" => "Ko",
            "Brazil" => "Es",
            "Spain" => "Es",
            "Mexico" => "Es",
            "Poland" => "Pl",
            "Germany" => "De",
            "Austria" => "De",
            "Switzerland" => "De",
            "Italy" => "It",
            "France" => "Fr",
            "Greece" => "El",
            "Russia" => "Ru",
            "Turkey" => "Tr",
            _ => throw new InvalidDataException($"No mapping from region {region} to language")
        };
}