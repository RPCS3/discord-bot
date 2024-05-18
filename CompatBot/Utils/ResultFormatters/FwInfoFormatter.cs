using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DSharpPlus.Entities;
using PsnClient.POCOs;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class FwInfoFormatter
{
    //2019_0828_c975768e5d70e105a72656f498cc9be9/PS3UPDAT.PUP
    [GeneratedRegex(@"(?<year>\d{4})_(?<month>\d\d)(?<day>\d\d)_(?<md5>[0-9a-f]+)", RegexOptions.ExplicitCapture | RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FwLinkInfo();
    private static readonly Dictionary<string, string> RegionToFlagMap = new(StringComparer.InvariantCultureIgnoreCase)
    {
        ["us"] = "🇺🇸",
        ["eu"] = "🇪🇺",
        ["uk"] = "🇬🇧",
        ["au"] = "🇦🇺",
        ["ru"] = "🇷🇺",
        ["jp"] = "🇯🇵",
        ["br"] = "🇧🇷",
        ["cn"] = "🇨🇳",
        ["hk"] = "🇭🇰",
        ["mx"] = "🇲🇽",
        ["sa"] = "🇸🇦",
        ["tw"] = "🇹🇼",
        ["kr"] = "🇰🇷",
    };

    public static DiscordEmbedBuilder ToEmbed(this List<FirmwareInfo> fwInfoList)
    {
        var result = new DiscordEmbedBuilder()
            .WithTitle("PS3 Firmware Information")
            .WithColor(Config.Colors.DownloadLinks);

        if (fwInfoList.Count > 0
            && fwInfoList.Select(fwi => FwLinkInfo().Match(fwi.DownloadUrl)).FirstOrDefault(m => m.Success) is Match info)
        {
            result.Description = $"Latest version is **{fwInfoList[0].Version}** released on {info.Groups["year"].Value}-{info.Groups["month"].Value}-{info.Groups["day"].Value}\n" +
                                 $"It is available in {fwInfoList.Count} region{(fwInfoList.Count == 1 ? "" : "s")} out of {RegionToFlagMap.Count}";
            result.AddField("Checksums", $"""
                    MD5: `{info.Groups["md5"].Value}`
                    You can use [HashCheck](https://github.com/gurnec/HashCheck/releases/latest) to verify your download
                    """);
            var links = new StringBuilder();
            foreach (var fwi in fwInfoList)
            {
                var newLink = $"[{RegionToFlagMap[fwi.Locale]}]({fwi.DownloadUrl}) ";
                if (links.Length + newLink.Length > EmbedPager.MaxFieldLength)
                    break;

                links.Append(newLink);
            }
            result.AddField("System Software License Agreement", "You **must** read and agree with the terms described [here](https://doc.dl.playstation.net/doc/ps3-eula/) before downloading");
            result.AddField("Click a flag below to download the firmware", links.ToString().TrimEnd());
            return result.WithFooter("Every region has identical firmware content");
        }

        return result.WithColor(Config.Colors.CompatStatusUnknown);
    }
}
