using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class UpdateInfoFormatter
    {
        private static readonly Client client = new Client();

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo info, DiscordEmbedBuilder builder = null)
        {
            if ((info?.LatestBuild?.Windows?.Download ?? info?.LatestBuild?.Linux?.Download) == null)
                return builder ?? new DiscordEmbedBuilder {Title = "Error", Description = "Error communicating with the update API. Try again later.", Color = Config.Colors.Maintenance};

            var justAppend = builder != null;
            var build = info.LatestBuild;
            var pr = build?.Pr ?? "0";
            string url = null;
            PrInfo prInfo = null;

            if (!justAppend)
            {
                if (pr == "0")
                    pr = "PR #???";
                else
                {
                    url = "https://github.com/RPCS3/rpcs3/pull/" + pr;
                    prInfo = await client.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false);
                    pr = $"PR #{pr} by {prInfo?.User?.Login ?? "???"}";
                }
            }
            builder = builder ?? new DiscordEmbedBuilder {Title = pr, Url = url, Description = prInfo?.Title, Color = Config.Colors.DownloadLinks};
            return builder
                .AddField($"Windows ({build?.Windows?.Datetime})   ".FixSpaces(), GetLinkMessage(build?.Windows?.Download, true), true)
                .AddField($"Linux ({build?.Linux?.Datetime})   ".FixSpaces(), GetLinkMessage(build?.Linux?.Download, true), true);
        }

        private static string GetLinkMessage(string link, bool simpleName)
        {
            if (string.IsNullOrEmpty(link))
                return "No link available";

            var text = new Uri(link).Segments?.Last() ?? "";
            if (simpleName && text.StartsWith("rpcs3-"))
                text = text.Substring(6);
            if (simpleName && text.Contains('_'))
                text = text.Split('_', 2)[0] + Path.GetExtension(text);

            return $"⏬ [{text}]({link}){"   ".FixSpaces()}";
        }

    }
}
