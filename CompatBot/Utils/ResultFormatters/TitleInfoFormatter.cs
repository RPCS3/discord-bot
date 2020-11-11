using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatApiClient.POCOs;
using CompatBot.Database;
using CompatBot.Database.Providers;
using DSharpPlus.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class TitleInfoFormatter
	{
		private static readonly Dictionary<string, DiscordColor> StatusColors = new Dictionary<string, DiscordColor>(StringComparer.InvariantCultureIgnoreCase)
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

        private static string? ToPrString(this TitleInfo info, string? defaultString, bool link = false)
        {
            if ((info.Pr ?? 0) == 0)
                return defaultString;

            if (link)
                return $"[#{info.Pr}](https://github.com/RPCS3/rpcs3/pull/{info.Pr})";

            return $"#{info.Pr}";
        }

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
                result += $" (PR {info.ToPrString("#????"),-5})`";
                if (info.Thread > 0)
                    result += $" <https://forums.rpcs3.net/thread-{info.Thread}.html>";
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
                using var db = new ThumbnailDb();
                var thumb = db.Thumbnail.FirstOrDefault(t => t.ProductCode == titleId);
                if (thumb?.CompatibilityStatus != null)
                {
                    info = new TitleInfo
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
                var onlineOnlypart = info.Network == 1 ? " 🌐" : "";
                var pr = info.ToPrString(null, true);
                var desc = $"{info.Status} since {info.ToUpdated() ?? "forever"}";
                if (pr != null)
                    desc += $" (PR {pr})";
                if (!forLog && !string.IsNullOrEmpty(info.AlternativeTitle))
                    desc = info.AlternativeTitle + Environment.NewLine + desc;
                if (!string.IsNullOrEmpty(info.WikiTitle))
                    desc += $"{(forLog ? ", " : Environment.NewLine)}[Wiki Page](https://wiki.rpcs3.net/index.php?title={info.WikiTitle})";
                if (info.UsingLocalCache == true)
                    desc += " (cached)";
                var cacheTitle = info.Title ?? gameTitle;
                if (!string.IsNullOrEmpty(cacheTitle))
                {
                    StatsStorage.GameStatCache.TryGetValue(cacheTitle, out int stat);
                    StatsStorage.GameStatCache.Set(cacheTitle, ++stat, StatsStorage.CacheTime);
                }
                var title = $"{productCodePart}{cacheTitle?.Trim(200)}{onlineOnlypart}";
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
                    StatsStorage.GameStatCache.TryGetValue(gameTitle, out int stat);
                    StatsStorage.GameStatCache.Set(gameTitle, ++stat, StatsStorage.CacheTime);
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
}
