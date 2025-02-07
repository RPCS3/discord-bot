using System;
using CompatApiClient.Utils;
using DSharpPlus.Entities;
using IrdLibraryClient;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompatApiClient;
using IrdLibraryClient.POCOs;

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
            if (irdInfos is not {Count: >0})
            {
                result.Color = Config.Colors.LogResultFailed;
                result.Description = "No matches were found";
                return result;
            }
            
            foreach (var item in irdInfos.Where(i => i.Link is {Length: >5}).Take(EmbedPager.MaxFields))
            {
                try
                {
                    result.AddField(
                        $"{item.Title.Sanitize().Trim(EmbedPager.MaxFieldTitleLength - 18)} [v{item.GameVer} FW {item.FwVer}]",
                        $"[⏬ {Path.GetFileName(item.Link).Replace("]", @"\]")}]({IrdClient.GetEscapedDownloadLink(item.Link)})"
                    );
                }
                catch (Exception e)
                {
                    ApiConfig.Log.Warn(e, "Failed to format embed field for IRD search result");
                }
            }
            return result;
        }
    }
}
