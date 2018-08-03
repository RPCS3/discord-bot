using System;
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

        public static async Task<IReadOnlyCollection<DiscordMessage>> GetMessagesBeforeAsync(this DiscordChannel channel, ulong beforeMessageId, int limit = 100, DateTime? timeLimit = null)
        {
            if (timeLimit > DateTime.UtcNow)
                throw new ArgumentException(nameof(timeLimit));

            var afterTime = timeLimit ?? DateTime.UtcNow.AddSeconds(-30);
            var messages = await channel.GetMessagesBeforeAsync(beforeMessageId, limit).ConfigureAwait(false);
            return messages.TakeWhile(m => m.CreationTimestamp > afterTime).ToList().AsReadOnly();
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, string trigger, string context, bool needsAttention = false)
        {
            var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
            var embedBuilder = MakeReportTemplate(infraction, message, needsAttention);
            var reportText = string.IsNullOrEmpty(trigger) ? "" : $"Triggered by: `{trigger}`{Environment.NewLine}";
            if (!string.IsNullOrEmpty(context))
                reportText += $"Triggered in: ```{context.Sanitize()}```{Environment.NewLine}";
            embedBuilder.Description = reportText + embedBuilder.Description;
            var logChannel = await getLogChannelTask.ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        public static async Task<DiscordMessage> ReportAsync(this DiscordClient client, string infraction, DiscordMessage message, IEnumerable<DiscordUser> reporters, bool needsAttention = true)
        {
            var getLogChannelTask = client.GetChannelAsync(Config.BotLogId);
            var embedBuilder = MakeReportTemplate(infraction, message, needsAttention);
            embedBuilder.AddField("Reporters", string.Join(Environment.NewLine, reporters.Select(r => r.Mention)));
            var logChannel = await getLogChannelTask.ConfigureAwait(false);
            return await logChannel.SendMessageAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        private static DiscordEmbedBuilder MakeReportTemplate(string infraction, DiscordMessage message, bool needsAttention){
            var content = message.Content;
            if (message.Attachments.Any())
            {
                if (!string.IsNullOrEmpty(content))
                    content += Environment.NewLine;
                content += string.Join(Environment.NewLine, message.Attachments.Select(a => "📎 " + a.FileName));
            }
            if (string.IsNullOrEmpty(content))
                content = "🤔 something fishy is going on here, there was no message or attachment";
            var result = new DiscordEmbedBuilder
                {
                    Title = infraction,
                    Description = needsAttention ? "Not removed, requires attention! @here" : "Removed, doesn't require attention",
                    Color = needsAttention ? Config.Colors.LogAlert : Config.Colors.LogNotice
                }.AddField("Violator", message.Author.Mention, true)
                .AddField("Channel", message.Channel.Mention, true)
                .AddField("Time (UTC)", message.CreationTimestamp.ToString("yyyy-MM-dd HH:mm:ss"), true)
                .AddField("Content of the offending item", content);
            if (needsAttention)
                result.AddField("Link to the message", $"https://discordapp.com/channels/{message.Channel.Guild.Id}/{message.Channel.Id}/{message.Id}");
            return result;
        }
    }
}
