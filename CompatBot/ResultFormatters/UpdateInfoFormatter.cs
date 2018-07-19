using System;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using DSharpPlus.Entities;

namespace CompatBot.ResultFormatters
{
    internal static class UpdateInfoFormatter
    {
        private static readonly Client client = new Client();

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this UpdateInfo info, DiscordEmbedBuilder builder = null)
        {
            var justAppend = builder != null;
            var build = info?.LatestBuild;
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
                    pr = $"PR #{pr} by {prInfo?.User?.login ?? "???"}";
                }
            }
            builder = builder ?? new DiscordEmbedBuilder {Title = pr, Url = url, Description = prInfo?.Title, Color = Config.Colors.DownloadLinks};
            return builder
                .AddField($"Windows ({build?.Windows?.Datetime})", GetLinkMessage(build?.Windows?.Download, justAppend), justAppend)
                .AddField($"Linux ({build?.Linux?.Datetime})", GetLinkMessage(build?.Linux?.Download, justAppend), justAppend);
        }

        private static string GetLinkMessage(string link, bool simpleName)
        {
            if (string.IsNullOrEmpty(link))
                return "No link available";

            var text = new Uri(link).Segments?.Last();
            if (simpleName && text.Contains('_'))
                text = text.Split('_', 2)[0];
                
            return $"⏬ [{text}]({link})";
        }

    }
}
