using System.Globalization;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;

namespace CompatBot.Utils.ResultFormatters;

internal static class TitleInfoFormatter
{
    private static readonly Dictionary<string, DiscordColor> StatusColors = new(StringComparer.InvariantCultureIgnoreCase)
    {
        {"Unknown", Config.Colors.CompatStatusUnknown},
        {"Nothing", Config.Colors.CompatStatusNothing},
        {"Loadable", Config.Colors.CompatStatusLoadable},
        {"Intro", Config.Colors.CompatStatusIntro},
        {"Ingame", Config.Colors.CompatStatusIngame},
        {"Playable", Config.Colors.CompatStatusPlayable},
    };

    public static string? ToUpdated(this TitleInfo info)
        => DateTime.TryParseExact(info.Date, ApiConfig.DateInputFormat, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToString(ApiConfig.DateOutputFormat)
            : null;

    private static string ToPrString(this TitleInfo info)
        => $"[#{info.Pr}](<https://github.com/RPCS3/rpcs3/pull/{info.Pr}>)";

    public static string AsString(this TitleInfo info, string titleId)
    {
        if (info.Status == TitleInfo.Maintenance.Status)
            return "API is undergoing maintenance, please try again later.";

        if (info.Status == TitleInfo.CommunicationError.Status)
            return "Error communicating with compatibility API, please try again later.";

        if (StatusColors.TryGetValue(info.Status, out _))
        {
            var title = info.Title.StripMarks().Trim(40);
            var result = $"{StringUtils.InvisibleSpacer}`[{titleId,-9}] {title,-40} {info.Status,8}";
            if (string.IsNullOrEmpty(info.Date))
                result += "                 ";
            else
                result += $" since {info.ToUpdated(),-10}";
            result += '`';
            if (info.Pr > 0)
                result += $" PR {info.ToPrString(),-5}";
            if (info.Thread > 0)
                result += $", [forum](<https://forums.rpcs3.net/thread-{info.Thread}.html>)";
            return result;
        }

        return $"Product code {titleId} was not found in compatibility database";
    }

    public static DiscordEmbedBuilder AsEmbed(this TitleInfo info, string? titleId, string? gameTitle = null, bool forLog = false, string? thumbnailUrl = null)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
            gameTitle = null;
        titleId = titleId?.ToUpperInvariant();
        var productCodePart = string.IsNullOrWhiteSpace(titleId) ? "" : $"[{titleId}] ";
        if (!StatusColors.TryGetValue(info.Status, out _) && !string.IsNullOrEmpty(titleId))
        {
            using var db = ThumbnailDb.OpenRead();
            var thumb = db.Thumbnail.FirstOrDefault(t => t.ProductCode == titleId);
            if (thumb?.CompatibilityStatus != null)
            {
                info = new()
                {
                    Date = thumb.CompatibilityChangeDate?.AsUtc().ToString("yyyy-MM-dd"),
                    Status = thumb.CompatibilityStatus.ToString(),
                    Title = thumb.Name,
                    UsingLocalCache = true,
                };
            }
        }
        if (info.Status is string status && StatusColors.TryGetValue(status, out var color))
        {
            // apparently there's no formatting in the footer, but you need to escape everything in description; ugh
            var onlineOnlyPart = info.Network == 1 ? " 🌐" : "";
            var desc = $"{info.Status} since {info.ToUpdated() ?? "forever"}";
            if (info.Pr > 0)
                desc += $" (PR {info.ToPrString()})";
            if (!forLog && !string.IsNullOrEmpty(info.AlternativeTitle))
                desc = info.AlternativeTitle + Environment.NewLine + desc;
            if (!string.IsNullOrEmpty(info.WikiTitle))
                desc += $"{(forLog ? ", " : Environment.NewLine)}[Wiki Page](https://wiki.rpcs3.net/index.php?title={Uri.EscapeDataString(info.WikiTitle)})";
            if (info.UsingLocalCache == true)
                desc += " (cached)";
            var cacheTitle = info.Title ?? gameTitle;
            if (!string.IsNullOrEmpty(cacheTitle))
                StatsStorage.IncGameStat(cacheTitle);
            var title = $"{productCodePart}{cacheTitle?.Trim(200)}{onlineOnlyPart}";
            if (string.IsNullOrEmpty(title))
                desc = "";
            return new DiscordEmbedBuilder
            {
                Title = title,
                Url = info.Thread > 0 ? $"https://forums.rpcs3.net/thread-{info.Thread}.html" : null,
                Description = desc,
                Color = color,
            }.WithThumbnail(thumbnailUrl);
        }
        else
        {
            var desc = "";
            var embedColor = Config.Colors.Maintenance;
            if (info.Status == TitleInfo.Maintenance.Status)
                desc = "API is undergoing maintenance, please try again later.";
            else if (info.Status == TitleInfo.CommunicationError.Status)
                desc = "Error communicating with compatibility API, please try again later.";
            else
            {
                embedColor = Config.Colors.CompatStatusUnknown;
                if (!string.IsNullOrEmpty(titleId))
                    desc = $"Product code {titleId} was not found in compatibility database";
            }
            var result = new DiscordEmbedBuilder
            {
                Description = desc,
                Color = embedColor,
            }.WithThumbnail(thumbnailUrl);
            if (gameTitle == null
                && !string.IsNullOrEmpty(titleId)
                && ThumbnailProvider.GetTitleNameAsync(titleId, Config.Cts.Token).ConfigureAwait(false).GetAwaiter().GetResult() is string titleName
                && !string.IsNullOrEmpty(titleName))
                gameTitle = titleName;
            if (!string.IsNullOrEmpty(gameTitle))
            {
                StatsStorage.IncGameStat(gameTitle);
                result.Title = $"{productCodePart}{gameTitle.Sanitize().Trim(200)}";
            }
            return result;
        }
    }

    public static string AsString(this (string code, TitleInfo info, double score) resultInfo)
        => resultInfo.info.AsString(resultInfo.code);

    public static string AsString(this KeyValuePair<string, TitleInfo> resultInfo)
        => resultInfo.Value.AsString(resultInfo.Key);

    public static DiscordEmbed AsEmbed(this KeyValuePair<string, TitleInfo> resultInfo)
        => resultInfo.Value.AsEmbed(resultInfo.Key);
}