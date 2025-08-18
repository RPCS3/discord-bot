using System.IO;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.ThumbScrapper;
using PsnClient.POCOs;

namespace CompatBot.Utils.ResultFormatters;

internal static class TitlePatchFormatter
{
    // thanks BCES00569
    public static async Task<List<DiscordMessageBuilder>> AsMessageAsync(this TitlePatch? patch, DiscordClient client, string productCode)
    {
        var pkgs = patch?.Tag?.Packages;
        var title = pkgs?.Select(p => p.ParamSfo?.Title).LastOrDefault(t => !string.IsNullOrEmpty(t))
                    ?? await ThumbnailProvider.GetTitleNameAsync(productCode, Config.Cts.Token).ConfigureAwait(false)
                    ?? productCode;
        title = title.Replace('\r', ' ').Replace('\n', ' ').Replace("  ", " ");
        var content = new StringBuilder();
        var thumbnailUrl = await client.GetThumbnailUrlAsync(productCode).ConfigureAwait(false);
        if (pkgs is {Length: >0})
        {
            content.AppendLine($"### {title}");
            if (pkgs.Length > 1)
                    content.AppendLine(
                        $"""
                        ℹ️ Total download size of all {pkgs.Length} packages is {pkgs.Sum(p => p.Size).AsStorageUnit()}.
                        ⏩ You can use tools such as [rusty-psn](https://github.com/RainbowCookie32/rusty-psn/releases/latest) or [PySN](https://github.com/AphelionWasTaken/PySN/releases/latest) for mass download of all updates.

                        ⚠️ You **must** install listed updates in order, starting with the first one. You **can not** skip intermediate versions.
                        """
                    ).AppendLine();
            foreach (var pkg in pkgs)
                content.AppendLine($"""[⏬ Update v`{pkg.Version}` ({pkg.Size.AsStorageUnit()})]({pkg.Url})""");
        }
        else if (pkgs is [var pkg])
        {
            content.AppendLine(
                $"""
                 ### {title} update v{pkg.Version} ({pkg.Size.AsStorageUnit()})
                 [⏬ {Path.GetFileName(GetLinkName(pkg.Url))}]({pkg.Url})
                 """
            );
        }
        else
            content.AppendLine($"### {title}")
                .AppendLine("No updates available");
        if (patch?.OfflineCacheTimestamp is DateTime cacheTimestamp)
            content.AppendLine()
                .AppendLine($"-# Offline cache, last updated {(DateTime.UtcNow - cacheTimestamp).AsTimeDeltaDescription()} ago");

        var result = new List<DiscordMessageBuilder>();
        IReadOnlyList<DiscordTextDisplayComponent> contentParts = Split(content);
        foreach (var page in contentParts)
        {
            IReadOnlyList<DiscordComponent> msgBody;
            if (thumbnailUrl is { Length: > 0 })
                msgBody =
                [
                    new DiscordSectionComponent([page], new DiscordThumbnailComponent(thumbnailUrl))
                ];
            else
                msgBody = [page];
            var msgBuilder = new DiscordMessageBuilder()
                .EnableV2Components()
                .AddContainerComponent(
                    new(
                        msgBody,
                        color: Config.Colors.DownloadLinks
                    )
                );
            result.Add(msgBuilder);
        }
        return result;
    }

    private static List<DiscordTextDisplayComponent> Split(StringBuilder content)
    {
        var lines = content.ToString().TrimEnd().Split(Environment.NewLine);
        var isMultiPage = content.Length > 4001;
        var title = lines[0];
        var result = new List<DiscordTextDisplayComponent>();
        content.Clear();
        content.Append(title);
        if (isMultiPage)
            content.Append(" [Page 1 of 2]");
        foreach (var l in lines.Skip(1))
        {
            check:
            if (content.Length + l.Length + 1 <= 4000)
                content.Append('\n').Append(l);
            else if (content.Length is 0)
                content.Append(l.Trim(4000));
            else
            {
                result.Add(new(content.ToString()));
                content.Clear();
                content.Append(title).Append(" [Page 2 of 2]");
                goto check;
            }
        }
        result.Add(new(content.ToString()));
        return result;
    }

    private static string GetLinkName(string link)
    {
        var fname = Path.GetFileName(link);
        if (fname.EndsWith("-PE.pkg"))
            fname = fname[..^7] + fname[^4..];
        try
        {
            var match = PsnScraper.ContentIdMatcher().Match(fname);
            if (match.Success)
                return fname[20..];
        }
        catch { }
        return fname;
    }
}