﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;

namespace CompatBot.Utils
{
    public static class DiscordClientExtensions
    {
        public static async Task<DiscordChannel> CreateDmAsync(this CommandContext ctx)
        {
            return ctx.Channel.IsPrivate ? ctx.Channel : await ctx.Member.CreateDmChannelAsync().ConfigureAwait(false);
        }

        public static Task<string> GetUserNameAsync(this CommandContext ctx, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
        {
            return ctx.Client.GetUserNameAsync(ctx.Channel, userId, forDmPurposes, defaultName);
        }

        public static DiscordMember GetMember(this DiscordClient client, DiscordGuild guild, DiscordUser user)
        {
            return (from g in client.Guilds
                    where g.Key == guild.Id
                    from u in g.Value.Members
                    where u.Id == user.Id
                    select u
                ).FirstOrDefault();
        }

        public static DiscordMember GetMember(this DiscordClient client, DiscordUser user)
        {
            return (from g in client.Guilds
                    from u in g.Value.Members
                    where u.Id == user.Id
                    select u
                ).FirstOrDefault();
        }

        public static async Task<string> GetUserNameAsync(this DiscordClient client, DiscordChannel channel, ulong userId, bool? forDmPurposes = null, string defaultName = "Unknown user")
        {
            var isPrivate = forDmPurposes ?? channel.IsPrivate;
            if (userId == 0)
                return "";

            try
            {
                return (await client.GetUserAsync(userId)).Username;
            }
            catch (NotFoundException)
            {
                return isPrivate ? $"@{userId}" : defaultName;
            }
        }

        public static async Task ReactWithAsync(this DiscordMessage message, DiscordClient client, DiscordEmoji emoji, string fallbackMessage = null, bool showBoth = false)
        {
            var canReact = message.Channel.IsPrivate || message.Channel.PermissionsFor(client.GetMember(message.Channel.Guild, client.CurrentUser)).HasPermission(Permissions.AddReactions);
            if (canReact)
                await message.CreateReactionAsync(emoji).ConfigureAwait(false);
            if ((!canReact || showBoth) && !string.IsNullOrEmpty(fallbackMessage))
                await message.Channel.SendMessageAsync(fallbackMessage).ConfigureAwait(false);
        }

        public static Task ReactWithAsync(this CommandContext ctx, DiscordEmoji emoji, string fallbackMessage = null, bool showBoth = false)
        {
            return ReactWithAsync(ctx.Message, ctx.Client, emoji, fallbackMessage, showBoth);
        }

        public static async Task<IReadOnlyCollection<DiscordMessage>> GetMessagesBeforeAsync(this DiscordChannel channel, ulong beforeMessageId, int limit = 100, DateTime? timeLimit = null)
        {
            if (timeLimit > DateTime.UtcNow)
                throw new ArgumentException(nameof(timeLimit));

            var afterTime = timeLimit ?? DateTime.UtcNow.AddSeconds(-30);
            var messages = await channel.GetMessagesBeforeAsync(beforeMessageId, limit).ConfigureAwait(false);
            return messages.TakeWhile(m => m.CreationTimestamp > afterTime).ToList().AsReadOnly();
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, string trigger, string context, ReportSeverity severity)
        {
            var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
            var embedBuilder = MakeReportTemplate(client, infraction, message, severity);
            var reportText = string.IsNullOrEmpty(trigger) ? "" : $"Triggered by: `{trigger}`{Environment.NewLine}";
            if (!string.IsNullOrEmpty(context))
                reportText += $"Triggered in: ```{context.Sanitize()}```{Environment.NewLine}";
            embedBuilder.Description = reportText + embedBuilder.Description;
            var logChannel = await getLogChannelTask.ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, IEnumerable<DiscordUser> reporters, string comment, ReportSeverity severity)
        {
            var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
            var embedBuilder = MakeReportTemplate(client, infraction, message, severity);
            var reportText = string.IsNullOrEmpty(comment) ? "" : comment.Sanitize() + Environment.NewLine;
            embedBuilder.Description = (reportText + embedBuilder.Description).Trim(EmbedPager.MaxDescriptionLength);
            var members = reporters.Select(client.GetMember);
            embedBuilder.AddField("Reporters", string.Join(Environment.NewLine, members.Select(GetMentionWithNickname)));
            var logChannel = await getLogChannelTask.ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, string description, IEnumerable<DiscordMember> potentialVictims, ReportSeverity severity)
        {
            var result = new DiscordEmbedBuilder
            {
                Title = infraction,
                Color = GetColor(severity),
                Description = description.Trim(EmbedPager.MaxDescriptionLength),
            }.AddField("Potential Targets", string.Join(Environment.NewLine, potentialVictims.Select(GetMentionWithNickname)).Trim(EmbedPager.MaxFieldLength));
            var logChannel = await client.GetChannelAsync(Config.BotLogId).ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: result.Build()).ConfigureAwait(false);
        }

        public static string GetMentionWithNickname(this DiscordMember member)
        {
            return string.IsNullOrEmpty(member.Nickname) ? member.Mention : $"{member.Mention} ({member.Nickname.Sanitize()})";
        }

        private static DiscordEmbedBuilder MakeReportTemplate(DiscordClient client, string infraction, DiscordMessage message, ReportSeverity severity)
        {
            var content = message.Content;
            var needsAttention = severity > ReportSeverity.Low;
            if (message.Attachments.Any())
            {
                if (!string.IsNullOrEmpty(content))
                    content += Environment.NewLine;
                content += string.Join(Environment.NewLine, message.Attachments.Select(a => "📎 " + a.FileName));
            }
            if (string.IsNullOrEmpty(content))
                content = "🤔 something fishy is going on here, there was no message or attachment";
            var author = client.GetMember(message.Author);
            var result = new DiscordEmbedBuilder
                {
                    Title = infraction,
                    Color = GetColor(severity),
                }.AddField("Violator", GetMentionWithNickname(author), true)
                .AddField("Channel", message.Channel.Mention, true)
                .AddField("Time (UTC)", message.CreationTimestamp.ToString("yyyy-MM-dd HH:mm:ss"), true)
                .AddField("Content of the offending item", content);
            if (needsAttention)
                result.AddField("Link to the message", message.JumpLink.ToString());
            return result;
        }

        private static DiscordColor GetColor(ReportSeverity severity)
        {
            switch (severity)
            {
                case ReportSeverity.Low: return Config.Colors.LogInfo;
                case ReportSeverity.Medium: return Config.Colors.LogNotice;
                case ReportSeverity.High: return Config.Colors.LogAlert;
                default: return Config.Colors.LogUnknown;
            }
        }
    }
}
