using CompatApiClient.Utils;
using DSharpPlus.Entities;
using IrdLibraryClient;
using IrdLibraryClient.POCOs;

namespace CompatBot.Utils.ResultFormatters
{
    public static class IrdSearchResultFormattercs
    {
        public static DiscordEmbedBuilder AsEmbed(this SearchResult searchResult)
        {
            var result = new DiscordEmbedBuilder
            {
                //Title = "IRD Library Search Result",
                Color = Config.Colors.DownloadLinks,
            };
            if (searchResult.Data.Count == 0)
            {
                result.Color = Config.Colors.LogResultFailed;
                result.Description = "No matches were found";
                return result;
            }

            foreach (var item in searchResult.Data)
            {
                var parts = item.Filename?.Split('-');
                if (parts == null)
                    parts = new string[] {null, null};
                else if (parts.Length == 1)
                    parts = new[] {null, item.Filename};
                result.AddField(
                    $"[{parts?[0]}] {item.Title?.Sanitize().Trim(EmbedPager.MaxFieldTitleLength)}",
                    $"⏬ [`{parts[1]?.Sanitize().Trim(200)}`]({IrdClient.GetDownloadLink(item.Filename)})　ℹ [Info]({IrdClient.GetInfoLink(item.Filename)})"
                );
            }
            return result;
        }
    }
}
