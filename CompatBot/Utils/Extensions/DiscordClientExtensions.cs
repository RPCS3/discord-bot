using System;
using System.Collections.Generic;
using System.IO;
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
        public static DiscordMember GetMember(this DiscordClient client, DiscordGuild guild, ulong userId)
        {
            if (guild == null)
                return GetMember(client, userId);
            return (from g in client.Guilds
                    where g.Key == guild.Id
                    from u in g.Value.Members.Values
                    where u.Id == userId
                    select u
                ).FirstOrDefault();
        }

        public static DiscordMember GetMember(this DiscordClient client, DiscordGuild guild, DiscordUser user) => GetMember(client, guild, user.Id);

        public static DiscordMember GetMember(this DiscordClient client, ulong userId)
        {
            return (from g in client.Guilds
                    from u in g.Value.Members.Values
                    where u.Id == userId
                    select u
                ).FirstOrDefault();
        }

        public static DiscordMember GetMember(this DiscordClient client, DiscordUser user) => GetMember(client, user.Id);

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

        public static async Task RemoveReactionAsync(this DiscordMessage message, DiscordEmoji emoji)
        {
            try
            {
                await message.DeleteOwnReactionAsync(emoji).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        public static async Task ReactWithAsync(this DiscordMessage message, DiscordEmoji emoji, string fallbackMessage = null, bool? showBoth = null)
        {
            try
            {
                showBoth ??= message.Channel.IsPrivate;
                var canReact = message.Channel.IsPrivate || message.Channel.PermissionsFor(message.Channel.Guild.CurrentMember).HasPermission(Permissions.AddReactions);
                if (canReact)
                    await message.CreateReactionAsync(emoji).ConfigureAwait(false);
                if ((!canReact || showBoth.Value) && !string.IsNullOrEmpty(fallbackMessage))
                    await message.Channel.SendMessageAsync(fallbackMessage).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        public static Task RemoveReactionAsync(this CommandContext ctx, DiscordEmoji emoji)
        {
            return RemoveReactionAsync(ctx.Message, emoji);
        }

        public static Task ReactWithAsync(this CommandContext ctx, DiscordEmoji emoji, string fallbackMessage = null, bool? showBoth = null)
        {
            return ReactWithAsync(ctx.Message, emoji, fallbackMessage, showBoth ?? (ctx.Prefix == Config.AutoRemoveCommandPrefix));
        }

        public static async Task<IReadOnlyCollection<DiscordMessage>> GetMessagesBeforeAsync(this DiscordChannel channel, ulong beforeMessageId, int limit = 100, DateTime? timeLimit = null)
        {
            if (timeLimit > DateTime.UtcNow)
                throw new ArgumentException(nameof(timeLimit));

            var afterTime = timeLimit ?? DateTime.UtcNow.AddSeconds(-30);
            var messages = await channel.GetMessagesBeforeAsync(beforeMessageId, limit).ConfigureAwait(false);
            return messages.TakeWhile(m => m.CreationTimestamp > afterTime).ToList().AsReadOnly();
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, string trigger, string context, ReportSeverity severity, string actionList = null)
        {
            var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
            var embedBuilder = MakeReportTemplate(client, infraction, message, severity, actionList);
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

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, string description, ICollection<DiscordMember> potentialVictims, ReportSeverity severity)
        {
            var result = new DiscordEmbedBuilder
            {
                Title = infraction,
                Color = GetColor(severity),
                Description = description.Trim(EmbedPager.MaxDescriptionLength),
            };
            if (potentialVictims?.Count > 0)
                result.AddField("Potential Targets", string.Join(Environment.NewLine, potentialVictims.Select(GetMentionWithNickname)).Trim(EmbedPager.MaxFieldLength));
            var logChannel = await client.GetChannelAsync(Config.BotLogId).ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: result.Build()).ConfigureAwait(false);
        }

        public static string GetMentionWithNickname(this DiscordMember member)
        {
            return string.IsNullOrEmpty(member.Nickname) ? $"<@{member.Id}> (`{member.Username.Sanitize()}#{member.Discriminator}`)" : $"<@{member.Id}> (`{member.Username.Sanitize()}#{member.Discriminator}`, shown as `{member.Nickname.Sanitize()}`)";
        }

        public static string GetUsernameWithNickname(this DiscordMember member)
        {
            return string.IsNullOrEmpty(member?.Nickname) ? $"`{member?.Username.Sanitize()}`" : $"`{member.Username.Sanitize()}` (shown as `{member.Nickname.Sanitize()}`)";
        }

        public static DiscordEmoji GetEmoji(this DiscordClient client, string emojiName, string fallbackEmoji = null)
        {
            return GetEmoji(client, emojiName, fallbackEmoji == null ? null : DiscordEmoji.FromUnicode(fallbackEmoji));
        }

        public static DiscordEmoji GetEmoji(this DiscordClient client, string emojiName, DiscordEmoji fallbackEmoji)
        {
            try
            {
                if (!emojiName.StartsWith(":"))
                    emojiName = ":" + emojiName;
                if (!emojiName.EndsWith(":"))
                    emojiName = emojiName + ":";
                return DiscordEmoji.FromName(client, emojiName);
            }
            catch
            {
                return fallbackEmoji;
            }
        }

        public static Task SendMessageAsync(this DiscordChannel channel, string message, byte[] attachment, string filename)
        {
            if (!string.IsNullOrEmpty(filename) && attachment?.Length > 0)
                return channel.SendFileAsync(filename, new MemoryStream(attachment), message);
            else
                return channel.SendMessageAsync(message);
        }

        private static DiscordEmbedBuilder MakeReportTemplate(DiscordClient client, string infraction, DiscordMessage message, ReportSeverity severity, string actionList = null)
        {
            var content = message.Content;
            if (message.Channel.IsPrivate)
                severity = ReportSeverity.None;
            var needsAttention = severity > ReportSeverity.Low;
            if (message.Embeds?.Any() ?? false)
            {
                if (!string.IsNullOrEmpty(content))
                    content += Environment.NewLine;

                var srcEmbed = message.Embeds.First();
                content += $"🔤 {srcEmbed.Title}";
                if (srcEmbed.Fields?.Any() ?? false)
                    content += $"{Environment.NewLine}{srcEmbed.Description}{Environment.NewLine}+{srcEmbed.Fields.Count} fields";
            }
            if (message.Attachments?.Any() ?? false)
            {
                if (!string.IsNullOrEmpty(content))
                    content += Environment.NewLine;
                content += string.Join(Environment.NewLine, message.Attachments.Select(a => "📎 " + a.FileName));
            }

            if (string.IsNullOrEmpty(content))
                content = "🤔 something fishy is going on here, there was no message or attachment";
            DiscordMember author = null;
            try
            {
                author = client.GetMember(message.Author);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to get the member info for user {message.Author.Id} ({message.Author.Username})");
            }
            var result = new DiscordEmbedBuilder
                {
                    Title = infraction,
                    Color = GetColor(severity),
                }.AddField("Violator", author == null ? message.Author.Mention : GetMentionWithNickname(author), true)
                .AddField("Channel",  message.Channel.IsPrivate ? "Bot's DM" : message.Channel.Mention, true)
                //.AddField("Time (UTC)", message.CreationTimestamp.ToString("yyyy-MM-dd HH:mm:ss"), true)
                .AddField("Content of the offending item", content);
            if (!string.IsNullOrEmpty(actionList))
                result.AddField("Filter Actions", actionList, true);
            if (needsAttention && !message.Channel.IsPrivate)
                result.AddField("Link to the message", message.JumpLink.ToString());
#if DEBUG
            result.WithFooter("Test bot instance");
#endif
            return result;
        }

        private static DiscordColor GetColor(ReportSeverity severity) =>
            severity switch
            {
                ReportSeverity.Low => Config.Colors.LogInfo,
                ReportSeverity.Medium => Config.Colors.LogNotice,
                ReportSeverity.High => Config.Colors.LogAlert,
                _ => Config.Colors.LogUnknown
            };
    }
}
