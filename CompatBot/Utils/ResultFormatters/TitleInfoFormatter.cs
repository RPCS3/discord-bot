using System;
using System.Collections.Generic;
using System.Globalization;
using CompatApiClient;
using CompatApiClient.Utils;
using CompatApiClient.POCOs;
using CompatBot.Database.Providers;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore.Sqlite.Query.ExpressionTranslators.Internal;

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

        private static string ToUpdated(this TitleInfo info)
        {
            return DateTime.TryParseExact(info.Date, ApiConfig.DateInputFormat, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var date) ? date.ToString(ApiConfig.DateOutputFormat) : null;
        }

        private static string ToPrString(this TitleInfo info, string defaultString)
        {
            return (info.Pr ?? 0) == 0 ? defaultString : info.Pr.ToString();
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
                return $"ID:{titleId,-9} Title:{title,-40} PR:{info.ToPrString("???"),-4} Status:{info.Status,-8} Updated:{info.ToUpdated(),-10}";
            }

            return $"Product code {titleId} was not found in compatibility database, possibly untested!";
        }

        public static DiscordEmbed AsEmbed(this TitleInfo info, string titleId, bool footer = true, string thumbnailUrl = null)
        {
            if (info.Status == TitleInfo.Maintenance.Status)
                return new DiscordEmbedBuilder{Description = "API is undergoing maintenance, please try again later.", Color = Config.Colors.Maintenance}.Build();

            if (info.Status == TitleInfo.CommunicationError.Status)
                return new DiscordEmbedBuilder{Description = "Error communicating with compatibility API, please try again later.", Color = Config.Colors.Maintenance}.Build();

            if (StatusColors.TryGetValue(info.Status, out var color))
            {
                footer = footer && string.IsNullOrEmpty(thumbnailUrl);
                // apparently there's no formatting in the footer, but you need to escape everything in description; ugh
                var pr = info.ToPrString(footer ? @"¯\_(ツ)_ /¯" : @"¯\\\_(ツ)\_ /¯");
                var desc = $"Status: {info.Status}, PR: {pr}, Updated: {info.ToUpdated()}";
                return new DiscordEmbedBuilder
                    {
                        Title = $"[{titleId}] {info.Title.Trim(200)}",
                        Url = $"https://forums.rpcs3.net/thread-{info.Thread}.html",
                        Description = footer ? null : desc,
                        Color = color,
                        ThumbnailUrl = thumbnailUrl
                    }.WithFooter(footer ? desc : null)
                    .Build();
            }
            else
            {
                var desc = string.IsNullOrEmpty(titleId)
                    ? "No product id was found; log might be corrupted, please reupload a new copy"
                    : $"Product code {titleId} was not found in compatibility database, possibly untested!";
                var result = new DiscordEmbedBuilder
                {
                    Description = desc,
                    Color = Config.Colors.CompatStatusUnknown,
                    ThumbnailUrl = thumbnailUrl,
                };
                if (ThumbnailProvider.GetTitleName(titleId) is string titleName && !string.IsNullOrEmpty(titleName))
                    result.Title = $"[{titleId}] {titleName.Sanitize().Trim(200)}";
                return result.Build();
            }

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
