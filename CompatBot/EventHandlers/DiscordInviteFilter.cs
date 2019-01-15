using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class DiscordInviteFilter
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline;
        private static readonly Regex InviteLink = new Regex(@"(https?://)?discord(((app\.com/invite|\.gg)/(?<invite_id>.*?))|(\.me/(?<me_id>.*?)))(\s|\W|$)", DefaultOptions);
        private static readonly Regex DiscordInviteLink = new Regex(@"(https?://)?discord(app\.com/invite|\.gg)/(?<invite_id>.*?)(\s|\W|$)", DefaultOptions);
        private static readonly Regex DiscordMeLink = new Regex(@"(https?://)?discord\.me/(?<me_id>.*?)(\s|$)", DefaultOptions);
        private static readonly HttpClient HttpClient = HttpClientFactory.Create(new CompressionMessageHandler());

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            args.Handled = !await CheckMessageForInvitesAsync(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task OnMessageUpdated(MessageUpdateEventArgs args)
        {
            args.Handled = !await CheckMessageForInvitesAsync(args.Client, args.Message).ConfigureAwait(false);
        }

        public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
        {
            try
            {
                var after = DateTime.UtcNow - Config.ModerationTimeThreshold;
                foreach (var channel in guild.Channels.Where(ch => !ch.IsCategory && ch.Type != ChannelType.Voice))
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
                Config.Log.Error(e);
            }
        }

        public static async Task<bool> CheckMessageForInvitesAsync(DiscordClient client, DiscordMessage message)
        {
            if (message.Channel.IsPrivate)
                return true;

            if (message.Author.IsBot)
                return true;

            if (message.Author.IsWhitelisted(client, message.Channel.Guild))
                return true;

            if (message.Reactions.Any(r => r.Emoji == Config.Reactions.Moderated && r.IsMe))
                return true;

            var (hasInvalidResults, invites) = await client.GetInvitesAsync(message.Content).ConfigureAwait(false);
            if (!hasInvalidResults && invites.Count == 0)
                return true;

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
                    Config.Log.Warn(e);
                    await client.ReportAsync("An unapproved discord invite", message, "In invalid or expired invite", null, ReportSeverity.Medium).ConfigureAwait(false);
                    await message.ReactWithAsync(
                        client,
                        Config.Reactions.Moderated,
                        $"{message.Author.Mention} please remove this expired or invalid invite, and refrain from posting it again until you have recieved an approval from a moderator.",
                        true
                    ).ConfigureAwait(false);
                }
                return false;
            }

            foreach (var invite in invites)
            {
                if (!await InviteWhitelistProvider.IsWhitelistedAsync(invite).ConfigureAwait(false))
                {
                    try
                    {
                        await message.DeleteAsync("Not a white-listed discord invite link").ConfigureAwait(false);
                        await client.ReportAsync("An unapproved discord invite", message, $"Invite {invite.Code} was resolved to the {invite.Guild.Name} server", null, ReportSeverity.Low).ConfigureAwait(false);
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} invites to other servers must be whitelisted first.\n" +
                                                               $"Please refrain from posting it again until you have received an approval from a moderator.").ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e);
                        await client.ReportAsync("An unapproved discord invite", message, $"Invite {invite.Code} was resolved to the {invite.Guild.Name} server", null, ReportSeverity.Medium).ConfigureAwait(false);
                        await message.ReactWithAsync(
                            client,
                            Config.Reactions.Moderated,
                            $"{message.Author.Mention} invites to other servers must be whitelisted first.\n" +
                            $"Please remove it and refrain from posting it again until you have received an approval from a moderator.",
                            true
                        ).ConfigureAwait(false);
                    }
                    return false;
                }
            }
            return true;
        }

        public static async Task<(bool hasInvalidInvite, List<DiscordInvite> invites)> GetInvitesAsync(this DiscordClient client, string message, bool tryMessageAsACode = false)
        {
            var inviteCodes = InviteLink.Matches(message).Select(m => m.Groups["invite_id"]?.Value).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
            var discordMeLinks = InviteLink.Matches(message).Select(m => m.Groups["me_id"]?.Value).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (inviteCodes.Count == 0 && discordMeLinks.Count == 0 && !tryMessageAsACode)
                return (false, new List<DiscordInvite>(0));

            var hasInvalidInvites = false;
            foreach (var meLink in discordMeLinks)
            {
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.me/" + meLink))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                        request.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RPCS3CompatibilityBot", "2.0"));
                        using (var response = await HttpClient.SendAsync(request))
                        {
                            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                if (string.IsNullOrEmpty(html))
                                    continue;

                                foreach (Match match in DiscordInviteLink.Matches(html))
                                    inviteCodes.Add(match.Groups["invite_id"].Value);
                            }
                            else
                            {
                                hasInvalidInvites = true;
                                Config.Log.Warn($"Got {response.StatusCode} from discord.me: {html}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e);
                }
            }

            if (tryMessageAsACode)
                inviteCodes.Add(message);
            inviteCodes = inviteCodes.Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();

            var result = new List<DiscordInvite>(inviteCodes.Count);
            foreach (var inviteCode in inviteCodes)
                try
                {
                    if (await client.GetInviteByCodeAsync(inviteCode).ConfigureAwait(false) is DiscordInvite invite)
                        result.Add(invite);
                }
                catch (Exception e)
                {
                    hasInvalidInvites = true;
                    Config.Log.Warn(e, $"Failed to get invite for code {inviteCode}");
                }
            return (hasInvalidInvites, result);
        }

    }
}
