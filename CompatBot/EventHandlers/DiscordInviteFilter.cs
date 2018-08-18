using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class DiscordInviteFilter
    {
        private static readonly Regex DiscordInviteLink = new Regex(
            @"https?://discord(app\.com/invite|\.gg)/(?<invite_id>.*?)(\s|$)",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline
        );

        public static Task OnMessageCreated(MessageCreateEventArgs args) => CheckMessageForInvitesAsync(args.Client, args.Message);
        public static Task OnMessageUpdated(MessageUpdateEventArgs args) => CheckMessageForInvitesAsync(args.Client, args.Message);

        public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
        {
            try
            {
                var after = DateTime.UtcNow - Config.ModerationTimeThreshold;
                foreach (var channel in guild.Channels.Where(ch => !ch.IsCategory))
                {
                    var messages = await channel.GetMessagesAsync(500).ConfigureAwait(false);
                    var messagesToCheck = from msg in messages
                        where msg.CreationTimestamp > after
                        select msg;
                    foreach (var message in messagesToCheck)
                        await CheckMessageForInvitesAsync(client, message).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "", e.ToString(), DateTime.Now);
            }
        }

        private static async Task CheckMessageForInvitesAsync(DiscordClient client, DiscordMessage message)
        {
            if (DefaultHandlerFilter.IsFluff(message))
                return;

            //if (message.Author.IsWhitelisted(client, message.Channel.Guild))
            //    return;

            if (message.Reactions.Any(r => r.Emoji == Config.Reactions.Moderated && r.IsMe))
                return;

            var (hasInvalidResults, invites) = await client.GetInvitesAsync(message.Content).ConfigureAwait(false);
            if (!hasInvalidResults && invites.Count == 0)
                return;

            if (hasInvalidResults)
            {
                try
                {
                    await message.DeleteAsync("Not a white-listed discord invite link").ConfigureAwait(false);
                    await client.ReportAsync("An unapproved discord invite", message, "In invalid or expired invite", null, ReportSeverity.Low).ConfigureAwait(false);
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} please refrain from posting invites that were not approved by a moderator, especially expired or invalid.").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    client.DebugLogger.LogMessage(LogLevel.Warning, "", e.ToString(), DateTime.Now);
                    await client.ReportAsync("An unapproved discord invite", message, "In invalid or expired invite", null, ReportSeverity.Medium).ConfigureAwait(false);
                    await message.ReactWithAsync(
                        client,
                        Config.Reactions.Moderated,
                        $"{message.Author.Mention} please remove this expired or invalid invite, and refrain from posting it again until you have recieved an approval from a moderator.",
                        true
                    ).ConfigureAwait(false);
                }
                return;
            }

            foreach (var invite in invites)
            {
                if (!await InviteWhitelistProvider.IsWhitelistedAsync(invite).ConfigureAwait(false))
                {
                    try
                    {
                        await message.DeleteAsync("Not a white-listed discord invite link").ConfigureAwait(false);
                        await client.ReportAsync("An unapproved discord invite", message, $"Invite {invite.Code} was resolved to the {invite.Guild.Name} server", null, ReportSeverity.Low).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} invites to the {invite.Guild.Name.Sanitize()} server are not whitelisted.\n" +
                                                               $"Please refrain from posting it again until you have recieved an approval from a moderator.").ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        client.DebugLogger.LogMessage(LogLevel.Warning, "", e.ToString(), DateTime.Now);
                        await client.ReportAsync("An unapproved discord invite", message, $"Invite {invite.Code} was resolved to the {invite.Guild.Name} server", null, ReportSeverity.Medium).ConfigureAwait(false);
                        await message.ReactWithAsync(
                            client,
                            Config.Reactions.Moderated,
                            $"{message.Author.Mention} invites to the {invite.Guild.Name.Sanitize()} server are not whitelisted.\n" +
                            $"Please remove it and refrain from posting it again until you have recieved an approval from a moderator.",
                            true
                        ).ConfigureAwait(false);
                    }
                    return;
                }
            }
        }

        public static async Task<(bool hasInvalidInvite, List<DiscordInvite> invites)> GetInvitesAsync(this DiscordClient client, string message, bool tryMessageAsACode = false)
        {
            var inviteCodes = DiscordInviteLink.Matches(message).Select(m => m.Groups["invite_id"].Value).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (inviteCodes.Count == 0 && !tryMessageAsACode)
                return (false, new List<DiscordInvite>(0));

            if (tryMessageAsACode)
                inviteCodes.Add(message);

            var result = new List<DiscordInvite>(inviteCodes.Count);
            var hasInvalidInvites = false;
            foreach (var inviteCode in inviteCodes)
                try
                {
                    if (await client.GetInviteByCodeAsync(inviteCode).ConfigureAwait(false) is DiscordInvite invite)
                        result.Add(invite);
                }
                catch (Exception e)
                {
                    hasInvalidInvites = true;
                    client.DebugLogger.LogMessage(LogLevel.Warning, "", $"Failed to get invite for code {inviteCode}: {e}", DateTime.Now);
                }
            return (hasInvalidInvites, result);
        }

    }
}
