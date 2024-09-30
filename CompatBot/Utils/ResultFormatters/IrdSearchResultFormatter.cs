using CompatApiClient.Utils;
using DSharpPlus.Entities;
using IrdLibraryClient;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CompatBot.Utils.ResultFormatters
{
    public static class IrdSearchResultFormatter
    {
        public static DiscordEmbedBuilder AsEmbed(this List<IrdInfo> irdInfos)
        {
            var result = new DiscordEmbedBuilder
            {
                // Title = "IRD Library Search Result",
                Color = Config.Colors.DownloadLinks,
            };
            if (irdInfos == null || !irdInfos.Any())
            {
                result.Color = Config.Colors.LogResultFailed;
                result.Description = "No matches were found";
                return result;
            }
            foreach (var item in irdInfos)
            {
                if (string.IsNullOrEmpty(item.Link))
                    continue;
                result.AddField(
                    $"{item.Title} [v{item.GameVer} FW {item.FwVer}]",
                    $"[⏬ {Path.GetFileName(item.Link)}]({IrdClient.GetDownloadLink(item.Link)})"
                );
            }
            return result;
        }
    }
}
