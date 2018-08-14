using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.ThumbScrapper;
using DSharpPlus;
using DSharpPlus.Entities;
using PsnClient.POCOs;
using CompatApiClient.Utils;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class TitlePatchFormatter
    {
        private const long UnderKB = 1000;
        private const long UnderMB = 1000 * 1024;
        private const long UnderGB = 1000 * 1024 * 1024;

        // thanks BCES00569
        public static async Task<List<DiscordEmbed>> AsEmbedAsync(this TitlePatch patch, DiscordClient client, string productCode)
        {
            var result = new List<DiscordEmbed>();
            var pkgs = patch?.Tag?.Packages;
            var title = pkgs?.Select(p => p.ParamSfo?.Title).LastOrDefault(t => !string.IsNullOrEmpty(t)) ?? ThumbnailProvider.GetTitleName(productCode) ?? productCode;
            var thumbnailUrl = await client.GetThumbnailUrlAsync(productCode).ConfigureAwait(false);
            var embedBuilder = new DiscordEmbedBuilder
            {
                Title = title,
                Color = Config.Colors.DownloadLinks,
                ThumbnailUrl = thumbnailUrl,
            };
            if (pkgs?.Length > 1)
            {
                var pages = pkgs.Length / EmbedPager.MaxFields + (pkgs.Length % EmbedPager.MaxFields == 0 ? 0 : 1);
                if (pages > 1)
                    embedBuilder.Title = $"{title} [Part 1 of {pages}]".Trim(EmbedPager.MaxTitleSize);
                embedBuilder.Description = $"Total download size of all packages is {pkgs.Sum(p => p.Size).AsStorageUnit()}";
                var i = 0;
                do
                {
                    var pkg = pkgs[i++];
                    embedBuilder.AddField($"Update v{pkg.Version} ({pkg.Size.AsStorageUnit()})", $"⏬ [{GetLinkName(pkg.Url)}]({pkg.Url})");
                    if (i % EmbedPager.MaxFields == 0)
                    {
                        result.Add(embedBuilder.Build());
                        embedBuilder = new DiscordEmbedBuilder
                        {
                            Title = $"{title} [Part {i/EmbedPager.MaxFields+1} of {pages}]".Trim(EmbedPager.MaxTitleSize),
                            Color = Config.Colors.DownloadLinks,
                            ThumbnailUrl = thumbnailUrl,
                        };
                    }
                } while (i < pkgs.Length);
            }
            else if (pkgs?.Length == 1)
            {
                embedBuilder.Title = $"{title} update v{pkgs[0].Version} ({pkgs[0].Size.AsStorageUnit()})";
                embedBuilder.Description = $"⏬ [{Path.GetFileName(GetLinkName(pkgs[0].Url))}]({pkgs[0].Url})";
            }
            else
                embedBuilder.Description = "No updates were found";
            if (embedBuilder.Fields.Count > 0)
                result.Add(embedBuilder.Build());
            return result;
        }

        private static string GetLinkName(string link)
        {
            var fname = Path.GetFileName(link);
            try
            {
                var match = PsnScraper.ContentIdMatcher.Match(fname);
                if (match.Success)
                    return fname.Substring(20);
            }
            catch { }
            return fname;
        }

        private static string AsStorageUnit(this long bytes)
        {
            if (bytes < UnderKB)
                return $"{bytes} byte{StringUtils.GetSuffix(bytes)}";
            if (bytes < UnderMB)
                return $"{bytes / 1024.0:0.##} KB";
            if (bytes < UnderGB)
                return $"{bytes / 1024.0 / 1024:0.##} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        }
    }
}
