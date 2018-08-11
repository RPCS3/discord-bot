using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.ThumbScrapper;
using DSharpPlus;
using DSharpPlus.Entities;
using PsnClient.POCOs;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class TitlePatchFormatter
    {
        private const long UnderKB = 1000;
        private const long UnderMB = 1000 * 1024;
        private const long UnderGB = 1000 * 1024 * 1024;


        public static async Task<DiscordEmbed> AsEmbedAsync(this TitlePatch patch, DiscordClient client, string productCode)
        {
            var pkgs = patch?.Tag?.Packages;
            var title = pkgs?.Select(p => p.ParamSfo?.Title).LastOrDefault(t => !string.IsNullOrEmpty(t)) ?? ThumbnailProvider.GetTitleName(productCode) ?? productCode;
            var thumbnailUrl = await client.GetThumbnailUrlAsync(productCode).ConfigureAwait(false);
            var result = new DiscordEmbedBuilder
            {
                Title = title,
                Color = Config.Colors.DownloadLinks,
                ThumbnailUrl = thumbnailUrl,
            };
            if (pkgs?.Length > 1)
            {
                result.Description = $"Total download size of all packages is {pkgs.Sum(p => p.Size).AsStorageUnit()}";
                foreach (var pkg in pkgs)
                {
                    result.AddField($"Update v{pkg.Version} ({pkg.Size.AsStorageUnit()})", $"⏬ [{GetLinkName(pkg.Url)}]({pkg.Url})");
                }
            }
            else if (pkgs?.Length == 1)
            {
                result.Title = $"{title} update v{pkgs[0].Version} ({pkgs[0].Size.AsStorageUnit()})";
                result.Description = $"⏬ [{Path.GetFileName(GetLinkName(pkgs[0].Url))}]({pkgs[0].Url})";
            }
            else
                result.Description = "No updates were found";
            return result.Build();
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
