using System;
using System.Collections.Generic;
using System.Globalization;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatApiClient.POCOs;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using DSharpPlus.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class TitleInfoFormatter
    {
        private static readonly Dictionary<string, DiscordColor> StatusColors = new Dictionary<string, DiscordColor>(StringComparer.InvariantCultureIgnoreCase)
        {
            {"Nothing", Config.Colors.CompatStatusNothing},
            {"Loadable", Config.Colors.CompatStatusLoadable},
            {"Intro", Config.Colors.CompatStatusIntro},
            {"Ingame", Config.Colors.CompatStatusIngame},
            {"Playable", Config.Colors.CompatStatusPlayable},
        };

        public static string ToUpdated(this TitleInfo info)
        {
            return DateTime.TryParseExact(info.Date, ApiConfig.DateInputFormat, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var date) ? date.ToString(ApiConfig.DateOutputFormat) : null;
        }

        private static string ToPrString(this TitleInfo info, string defaultString, bool link = false)
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
                var title = info.Title.Trim(40);
                return $"`[{titleId,-9}] {title,-40} {info.Status,8} since {info.ToUpdated(),-10} (PR {info.ToPrString("#????"),-5})` https://forums.rpcs3.net/thread-{info.Thread}.html";
            }

            return $"Product code {titleId} was not found in compatibility database";
        }

        public static DiscordEmbedBuilder AsEmbed(this TitleInfo info, string titleId, string gameTitle = null, bool forLog = false, string thumbnailUrl = null)
        {
            if (info.Status == TitleInfo.Maintenance.Status)
                return new DiscordEmbedBuilder{Description = "API is undergoing maintenance, please try again later.", Color = Config.Colors.Maintenance};

            if (info.Status == TitleInfo.CommunicationError.Status)
                return new DiscordEmbedBuilder{Description = "Error communicating with compatibility API, please try again later.", Color = Config.Colors.Maintenance};

            if (string.IsNullOrWhiteSpace(gameTitle))
                gameTitle = null;

            if (StatusColors.TryGetValue(info.Status, out var color))
            {
                // apparently there's no formatting in the footer, but you need to escape everything in description; ugh
                var productCodePart = string.IsNullOrWhiteSpace(titleId) ? "" : $"[{titleId}] ";
                var pr = info.ToPrString(null, true);
                var desc = $"{info.Status} since {info.ToUpdated()}";
                if (pr is string _)
                    desc += $" (PR {pr})";
                if (!forLog && !string.IsNullOrEmpty(info.AlternativeTitle))
                    desc = info.AlternativeTitle + Environment.NewLine + desc;
                if (!string.IsNullOrEmpty(info.WikiTitle))
                    desc +=  $"{(forLog ? ", " : Environment.NewLine)}[Wiki Page](https://wiki.rpcs3.net/index.php?title={info.WikiTitle})";
                var cacheTitle = info.Title ?? gameTitle;
                if (!string.IsNullOrEmpty(cacheTitle))
                {
                    BaseCommandModuleCustom.GameStatCache.TryGetValue(cacheTitle, out int stat);
                    BaseCommandModuleCustom.GameStatCache.Set(cacheTitle, ++stat, BaseCommandModuleCustom.CacheTime);
                }
                return new DiscordEmbedBuilder
                    {
                        Title = $"{productCodePart}{cacheTitle.Trim(200)}",
                        Url = $"https://forums.rpcs3.net/thread-{info.Thread}.html",
                        Description = desc,
                        Color = color,
                        ThumbnailUrl = thumbnailUrl
                    };
            }
            else
            {
                var desc = "";
                if (!string.IsNullOrEmpty(titleId))
                    desc = $"Product code {titleId} was not found in compatibility database";
                var result = new DiscordEmbedBuilder
                {
                    Description = desc,
                    Color = Config.Colors.CompatStatusUnknown,
                    ThumbnailUrl = thumbnailUrl,
                };

                if (gameTitle == null
                    && ThumbnailProvider.GetTitleNameAsync(titleId, Config.Cts.Token).ConfigureAwait(false).GetAwaiter().GetResult() is string titleName
                    && !string.IsNullOrEmpty(titleName))
                    gameTitle = titleName;
                if (!string.IsNullOrEmpty(gameTitle))
                {
                    BaseCommandModuleCustom.GameStatCache.TryGetValue(gameTitle, out int stat);
                    BaseCommandModuleCustom.GameStatCache.Set(gameTitle, ++stat, BaseCommandModuleCustom.CacheTime);
                    var productCodePart = string.IsNullOrEmpty(titleId) ? "" : $"[{titleId}] ";
                    result.Title = $"{productCodePart}{gameTitle.Sanitize().Trim(200)}";
                }
                return result;
            }
        }

        public static string AsString(this (string code, TitleInfo info, double score) resultInfo)
        {
            return resultInfo.info.AsString(resultInfo.code);
        }

        public static string AsString(this KeyValuePair<string, TitleInfo> resultInfo)
        {
            return resultInfo.Value.AsString(resultInfo.Key);
        }

        public static DiscordEmbed AsEmbed(this KeyValuePair<string, TitleInfo> resultInfo)
        {
            return resultInfo.Value.AsEmbed(resultInfo.Key);
        }
    }
}
