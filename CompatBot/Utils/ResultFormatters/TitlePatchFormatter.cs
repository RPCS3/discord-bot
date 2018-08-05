using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
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


        public static async Task<DiscordEmbed> AsEmbedAsync(this TitlePatch patch, DiscordClient client)
        {
            var pkgs = patch.Tag?.Packages;
            var title = pkgs?.Select(p => p.ParamSfo?.Title).LastOrDefault(t => !string.IsNullOrEmpty(t)) ?? patch.TitleId;
            var thumbnailUrl = await client.GetThumbnailUrlAsync(patch.TitleId).ConfigureAwait(false);
            var result = new DiscordEmbedBuilder
            {
                Title = title,
                Color = Config.Colors.DownloadLinks,
                ThumbnailUrl = thumbnailUrl,
            };
            if (pkgs.Length > 1)
            {
                result.Description = $"Total download size of all packages is {pkgs.Sum(p => p.Size).AsStorageUnit()}";
                foreach (var pkg in pkgs)
                    result.AddField($"Update v{pkg.Version} ({pkg.Size.AsStorageUnit()})", $"⏬ [{Path.GetFileName(pkg.Url)}]({pkg.Url})");

            }
            else
            {
                result.Title = $"{title} update v{pkgs[0].Version} ({pkgs[0].Size.AsStorageUnit()})";
                result.Description = $"⏬ [{Path.GetFileName(pkgs[0].Url)}]({pkgs[0].Url})";
            }
            return result.Build();
        }

        private static string AsStorageUnit(this long bytes)
        {
            if (bytes < UnderKB)
                return $"{bytes} byte{(bytes % 10 == 1 && bytes % 100 != 11 ? "" : "s")}";
            if (bytes < UnderMB)
                return $"{bytes / 1024.0:0.##} KB";
            if (bytes < UnderGB)
                return $"{bytes / 1024.0 / 1024:0.##} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:0.##} GB";
        }
    }
}
