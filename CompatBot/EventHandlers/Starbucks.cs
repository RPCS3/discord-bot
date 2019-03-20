using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class Starbucks
    {
        private static readonly DiscordEmoji M = DiscordEmoji.FromUnicode("Ⓜ");
        private static readonly DiscordEmoji RidM = DiscordEmoji.FromUnicode("🇲"); // that's :regional_indicator_m:, and not a regular M
        private static readonly DiscordEmoji RidG = DiscordEmoji.FromUnicode("🇬");
        private static readonly DiscordEmoji RidS = DiscordEmoji.FromUnicode("🇸");
        private static readonly DiscordEmoji[] MsgVar1 = {RidM, RidG, RidS};
        private static readonly DiscordEmoji[] MsgVar2 = {M, RidG, RidS};
        private static readonly DiscordEmoji RidN = DiscordEmoji.FromUnicode("🇳");
        private static readonly DiscordEmoji RidO = DiscordEmoji.FromUnicode("🇴");

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
                Config.Log.Error(e);
            }
        }

        private static async Task CheckMessageAsync(DiscordClient client, DiscordChannel channel, DiscordUser user, DiscordMessage message, DiscordEmoji emoji)
        {
            try
            {
                if (user.IsBot || channel.IsPrivate)
                    return;

                // in case it's not in cache and doesn't contain any info, including Author
                message = await channel.GetMessageAsync(message.Id).ConfigureAwait(false);
                if (emoji == Config.Reactions.Starbucks)
                    await CheckMediaTalkAsync(client, channel, message, emoji).ConfigureAwait(false);

                if (message.Reactions.Any(r => r.Emoji == RidS))
                    await CheckGameFansAsync(client, channel, message).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        private static async Task CheckMediaTalkAsync(DiscordClient client, DiscordChannel channel, DiscordMessage message, DiscordEmoji emoji)
        {
            if (!Config.Moderation.Channels.Contains(channel.Id))
                return;

            // message.Timestamp throws if it's not in the cache AND is in local time zone
            if (DateTime.UtcNow - message.CreationTimestamp > Config.ModerationTimeThreshold)
                return;

            if (message.Reactions.Any(r => r.Emoji == emoji && (r.IsMe || r.Count < Config.Moderation.StarbucksThreshold)))
                return;

            if (message.Author.IsWhitelisted(client, channel.Guild))
                return;

            var users = await message.GetReactionsAsync(emoji).ConfigureAwait(false);
            var members = users
                .Select(u => channel.Guild
                            .GetMemberAsync(u.Id)
                            .ContinueWith(ct => ct.IsCompletedSuccessfully ? ct : Task.FromResult((DiscordMember)null), TaskScheduler.Default))
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
            await client.ReportAsync(Config.Reactions.Starbucks + " Media talk report", message, reporters, null, ReportSeverity.Medium).ConfigureAwait(false);
        }


        private static async Task CheckGameFansAsync(DiscordClient client, DiscordChannel channel, DiscordMessage message)
        {
            var mood = client.GetEmoji(":sqvat:", "😒");
            if (message.Reactions.Any(r => r.Emoji == RidN && r.IsMe))
                return;

            var reactionMsg = message
                .Reactions
                .SkipWhile(r => r.Emoji != RidM && r.Emoji != M)
                .Select(r => r.Emoji)
                .ToList();
            var hit = false;
            for (var i =0; i< reactionMsg.Count - 2; i++)
                if ((reactionMsg[i] == RidM || reactionMsg[i] == M)
                    && reactionMsg[i + 1] == RidG
                    && reactionMsg[i + 2] == RidS)
                {
                    hit = true;
                    break;
                }
            if (hit)
            {
                await message.ReactWithAsync(client, mood).ConfigureAwait(false);
                await message.ReactWithAsync(client, RidN).ConfigureAwait(false);
                await message.ReactWithAsync(client, RidO).ConfigureAwait(false);
            }
        }

    }
}
