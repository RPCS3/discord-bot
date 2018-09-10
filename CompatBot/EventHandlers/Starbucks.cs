using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class Starbucks
    {
        public static Task Handler(MessageReactionAddEventArgs args)
        {
            return CheckMessageAsync(args.Client, args.Channel, args.User, args.Message, args.Emoji);
        }

        public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
        {
            try
            {
                var after = DateTime.UtcNow - Config.ModerationTimeThreshold;
                var checkTasks = new List<Task>();
                foreach (var channel in guild.Channels.Where(ch => Config.Moderation.Channels.Contains(ch.Id)))
                {
                    var messages = await channel.GetMessagesAsync().ConfigureAwait(false);
                    var messagesToCheck = from msg in messages
                        where msg.CreationTimestamp > after && msg.Reactions.Any(r => r.Emoji == Config.Reactions.Starbucks && r.Count >= Config.Moderation.StarbucksThreshold)
                        select msg;
                    foreach (var message in messagesToCheck)
                    {
                        var reactionUsers = await message.GetReactionsAsync(Config.Reactions.Starbucks).ConfigureAwait(false);
                        if (reactionUsers.Count > 0)
                            checkTasks.Add(CheckMessageAsync(client, channel, reactionUsers[0], message, Config.Reactions.Starbucks));
                    }
                }
                await Task.WhenAll(checkTasks).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "", e.ToString(), DateTime.Now);
            }
        }

        private static async Task CheckMessageAsync(DiscordClient client, DiscordChannel channel, DiscordUser user, DiscordMessage message, DiscordEmoji emoji)
        {
            try
            {
                if (user.IsBot || channel.IsPrivate)
                    return;

                if (emoji != Config.Reactions.Starbucks)
                    return;

                if (!Config.Moderation.Channels.Contains(channel.Id))
                    return;

                // message.Timestamp throws if it's not in the cache AND is in local time zone
                if (DateTime.UtcNow - message.CreationTimestamp > Config.ModerationTimeThreshold)
                    return;

                if (message.Reactions.Any(r => r.Emoji == emoji && (r.IsMe || r.Count < Config.Moderation.StarbucksThreshold)))
                    return;

                // in case it's not in cache and doesn't contain any info, including Author
                message = await channel.GetMessageAsync(message.Id).ConfigureAwait(false);
                if (message.Author.IsWhitelisted(client, channel.Guild))
                    return;

                var users = await message.GetReactionsAsync(emoji).ConfigureAwait(false);
                var members = users
                    .Select(u => channel.Guild
                                .GetMemberAsync(u.Id)
                                .ContinueWith(ct => ct.IsCompletedSuccessfully ? ct : Task.FromResult((DiscordMember)null)))
                    .ToList() //force eager task creation
                    .Select(t => t.Unwrap().ConfigureAwait(false).GetAwaiter().GetResult())
                    .Where(m => m != null)
                    .ToList();
                var reporters = new List<DiscordMember>(Config.Moderation.StarbucksThreshold);
                foreach (var member in members)
                {
                    if (member.IsCurrent)
                        return;

                    if (member.Roles.Any())
                        reporters.Add(member);
                }
                if (reporters.Count < Config.Moderation.StarbucksThreshold)
                    return;

                await message.ReactWithAsync(client, emoji).ConfigureAwait(false);
                await client.ReportAsync("User moderation report ⭐💵", message, reporters, null, ReportSeverity.Medium).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "", e.ToString(), DateTime.Now);
            }
        }
    }
}
