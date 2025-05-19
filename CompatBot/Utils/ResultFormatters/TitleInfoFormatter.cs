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

    public static async ValueTask<DiscordEmbedBuilder> AsEmbedAsync(
        this TitleInfo info,
        string? titleId,
        string? gameTitle = null,
        bool forLog = false,
        string thumbnailUrl = ""
    )
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
            gameTitle = null;
        titleId = titleId?.ToUpperInvariant();
        var productCodePart = string.IsNullOrWhiteSpace(titleId) ? "" : $"[{titleId}] ";
        if (!StatusColors.TryGetValue(info.Status, out _) && !string.IsNullOrEmpty(titleId))
        {
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
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
        if (titleId is {Length: 9})
            info.Languages = await DiscLanguageProvider.GetLanguageListAsync(titleId).ConfigureAwait(false);
        if (info.Status is string status && StatusColors.TryGetValue(status, out var color))
        {
            // apparently there's no formatting in the footer, but you need to escape everything in description; ugh
            var onlineOnlyPart = info.Network is 1 ? " 🌐" : "";
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
            var result = new DiscordEmbedBuilder
            {
                Title = title,
                Url = info.Thread > 0 ? $"https://forums.rpcs3.net/thread-{info.Thread}.html" : null,
                Description = desc,
                Color = color,
            }.WithThumbnail(thumbnailUrl);
            if (!forLog)
                result.WithLanguages(info.Languages);
            return result;
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
                if (titleId is {Length: >0})
                    desc = $"Product code {titleId} was not found in compatibility database";
            }
            var result = new DiscordEmbedBuilder
            {
                Description = desc,
                Color = embedColor,
            }.WithThumbnail(thumbnailUrl);
            if (!forLog)
                result.WithLanguages(info.Languages);
            if (gameTitle is null
                && titleId is {Length: >0}
                && await ThumbnailProvider.GetTitleNameAsync(titleId, Config.Cts.Token).ConfigureAwait(false) is {Length: >0} titleName)
                gameTitle = titleName;
            if (gameTitle is {Length: >0})
            {
                StatsStorage.IncGameStat(gameTitle);
                result.Title = $"{productCodePart}{gameTitle.Sanitize().Trim(200)}";
            }
            return result;
        }
    }

    public static DiscordEmbedBuilder WithLanguages(this DiscordEmbedBuilder embedBuilder, IReadOnlyCollection<string> languages)
    {
        if (languages is not {Count: >0})
            return embedBuilder;

        return embedBuilder.AddField(
            "Supported Languages",
            string.Join('\n', languages)
        );
    }

    public static string AsString(this (string code, TitleInfo info, double score) resultInfo)
        => resultInfo.info.AsString(resultInfo.code);
}