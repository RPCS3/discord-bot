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
        // thanks BCES00569
        public static async Task<List<DiscordEmbedBuilder>> AsEmbedAsync(this TitlePatch patch, DiscordClient client, string productCode)
        {
            var result = new List<DiscordEmbedBuilder>();
            var pkgs = patch?.Tag?.Packages;
            var title = pkgs?.Select(p => p.ParamSfo?.Title).LastOrDefault(t => !string.IsNullOrEmpty(t))
                        ?? await ThumbnailProvider.GetTitleNameAsync(productCode, Config.Cts.Token).ConfigureAwait(false)
                        ?? productCode;
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
                    embedBuilder.Title = $"{title} [Part 1 of {pages}]".Trim(EmbedPager.MaxFieldTitleLength);
                embedBuilder.Description = $"Total download size of all {pkgs.Length} packages is {pkgs.Sum(p => p.Size).AsStorageUnit()}";
                var i = 0;
                do
                {
                    var pkg = pkgs[i++];
                    embedBuilder.AddField($"Update v{pkg.Version} ({pkg.Size.AsStorageUnit()})", $"⏬ [{GetLinkName(pkg.Url)}]({pkg.Url})");
                    if (i % EmbedPager.MaxFields == 0)
                    {
                        result.Add(embedBuilder);
                        embedBuilder = new DiscordEmbedBuilder
                        {
                            Title = $"{title} [Part {i/EmbedPager.MaxFields+1} of {pages}]".Trim(EmbedPager.MaxFieldTitleLength),
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
            if (!result.Any() || embedBuilder.Fields.Any())
                result.Add(embedBuilder);
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
    }
}
