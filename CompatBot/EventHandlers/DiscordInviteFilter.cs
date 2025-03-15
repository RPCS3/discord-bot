using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CompatApiClient.Compression;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal static partial class DiscordInviteFilter
{
    [GeneratedRegex(@"(https?://)?discord((((app)?\.com/invite|\.gg)/(?<invite_id>[a-z0-9\-]+))|(\.me/(?<me_id>.*?))(\s|>|$))", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex InviteLink();
    [GeneratedRegex(@"name=""csrf-token"" content=""(?<csrf_token>\w+)""")]
    private static partial Regex CsrfTokenPattern();
    [GeneratedRegex(@"name=""serverEid"" value=""(?<server_eid>\w+)""")]
    private static partial Regex ServerEidPattern();
    private static readonly MemoryCache InviteCodeCache = new(new MemoryCacheOptions{ExpirationScanFrequency = TimeSpan.FromHours(1)});
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public static Task<bool> OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args) => CheckMessageInvitesAreSafeAsync(c, args.Message);
    public static Task<bool> OnMessageUpdated(DiscordClient c, MessageUpdatedEventArgs args) => CheckMessageInvitesAreSafeAsync(c, args.Message);

    public static async Task<bool> CheckMessageInvitesAreSafeAsync(DiscordClient client, DiscordMessage message)
    {
        if (message.Channel.IsPrivate)
            return true;

        if (message.Author.IsBotSafeCheck())
            return true;

#if !DEBUG
        if (await message.Author.IsWhitelistedAsync(client, message.Channel.Guild).ConfigureAwait(false))
            return true;
#endif

        if (message.Reactions.Any(r => r.Emoji == Config.Reactions.Moderated && r.IsMe))
            return true;

        var (hasInvalidResults, attemptedWorkaround, invites) = await client.GetInvitesAsync(message.Content, message.Author).ConfigureAwait(false);
        if (!hasInvalidResults && invites.Count == 0)
            return true;

        if (hasInvalidResults)
        {
            try
            {
                DeletedMessagesMonitor.RemovedByBotCache.Set(message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                await message.DeleteAsync("Not a white-listed discord invite link").ConfigureAwait(false);
                await client.ReportAsync("🛃 An unapproved discord invite", message, "In invalid or expired invite",  null, null, null, ReportSeverity.Low).ConfigureAwait(false);
                await message.Channel.SendMessageAsync($"{message.Author.Mention} please refrain from posting invites that were not approved by a moderator, especially expired or invalid.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                await client.ReportAsync("🛃 An unapproved discord invite", message, "In invalid or expired invite", null, null, null, ReportSeverity.Medium).ConfigureAwait(false);
                await message.ReactWithAsync(Config.Reactions.Moderated,
                    $"{message.Author.Mention} please remove this expired or invalid invite, and refrain from posting it again until you have received an approval from a moderator.",
                    true
                ).ConfigureAwait(false);
            }
            return false;
        }

        foreach (var invite in invites)
        {
            if (!await InviteWhitelistProvider.IsWhitelistedAsync(invite).ConfigureAwait(false))
            {
                if (!InviteCodeCache.TryGetValue(message.Author.Id, out HashSet<string>? recentInvites) || recentInvites is null)
                    recentInvites = new();
                var repeatedInvitePost = !recentInvites.Add(invite.Code);
                var circumventionAttempt = repeatedInvitePost && attemptedWorkaround;
                InviteCodeCache.Set(message.Author.Id, recentInvites, CacheDuration);
                var removed = false;
                try
                {
                    DeletedMessagesMonitor.RemovedByBotCache.Set(message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                    await message.DeleteAsync("Not a white-listed discord invite").ConfigureAwait(false);
                    removed = true;
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e);
                }

                var codeResolveMsg = $"Invite {invite.Code} was resolved to the {invite.Guild?.Name} server";
                var reportMsg = codeResolveMsg;
                string userMsg;
                if (circumventionAttempt)
                {
                    reportMsg += "\nAlso tried to workaround filter despite being asked not to do so.";
                    userMsg = $"{message.Author.Mention} you have been asked nicely to not post invites to this unapproved discord server before.";
                }
                else
                {
                    userMsg = $"{message.Author.Mention} invites to other servers must be whitelisted first.\n";
                    if (removed)
                        userMsg += "Please refrain from posting it again until you have received an approval from a moderator.";
                    else
                        userMsg += "Please remove it and refrain from posting it again until you have received an approval from a moderator.";
                }
                await client.ReportAsync("🛃 An unapproved discord invite", message, reportMsg, null, null, null, ReportSeverity.Low).ConfigureAwait(false);
                if (repeatedInvitePost || recentInvites.Count > 1)
                    try
                    {
                        var member = await client.GetMemberAsync(message.Channel.Guild, message.Author).ConfigureAwait(false);
                        if (member is not null)
                        {
                            await member.RemoveAsync("Multiple invites after being warned").ConfigureAwait(false);
                            return false;
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to kick user {await message.Author.GetUsernameWithNicknameAsync(client).ConfigureAwait(false)} for repeated invite spam");
                    }
                
                await message.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
                if (circumventionAttempt)
                    await Warnings.AddAsync(client, message, message.Author.Id, message.Author.Username, client.CurrentUser, "Attempted to circumvent discord invite filter", codeResolveMsg);
                return false;
            }
        }
        return true;
    }

    public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
    {
        try
        {
            var botMember = await client.GetMemberAsync(guild, client.CurrentUser).ConfigureAwait(false);
            if (botMember == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                botMember = await client.GetMemberAsync(guild, client.CurrentUser).ConfigureAwait(false);
                if (botMember == null)
                {
                    Config.Log.Error("Failed to resolve bot as the guild member for guild " + guild);
                    return;
                }
            }

            var after = DateTime.UtcNow - Config.ModerationBacklogThresholdInHours;
            foreach (var channel in guild.Channels.Values.Where(ch => !ch.IsCategory && ch.Type != DiscordChannelType.Voice))
            {
                var permissions = channel.PermissionsFor(botMember);
                if (!permissions.HasPermission(DiscordPermission.ReadMessageHistory))
                {
                    Config.Log.Warn($"No permissions to read message history in #{channel.Name}");
                    continue;
                }

                if (!permissions.HasPermission(DiscordPermission.ViewChannel))
                {
                    Config.Log.Warn($"No permissions to access #{channel.Name}");
                    continue;
                }

                try
                {
                    var messages = await channel.GetMessagesCachedAsync(100).ConfigureAwait(false);
                    var messagesToCheck = from msg in messages
                        where msg.CreationTimestamp > after
                        select msg;
                    foreach (var message in messagesToCheck)
                        await CheckMessageInvitesAreSafeAsync(client, message).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Some missing permissions in #{channel.Name}");
                }
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public static async Task<(bool hasInvalidInvite, bool attemptToWorkaround, List<DiscordInvite> invites)> GetInvitesAsync(this DiscordClient client, string message, DiscordUser? author = null, bool tryMessageAsACode = false)
    {
        if (string.IsNullOrEmpty(message))
            return (false, false, new(0));

        var inviteCodes = new HashSet<string>(InviteLink().Matches(message).Select(m => m.Groups["invite_id"].Value).Where(s => !string.IsNullOrEmpty(s)));
        var discordMeLinks = InviteLink().Matches(message).Select(m => m.Groups["me_id"].Value).Distinct().Where(s => !string.IsNullOrEmpty(s)).ToList();
        var attemptedWorkaround = false;
        if (author != null && InviteCodeCache.TryGetValue(author.Id, out HashSet<string>? recentInvites) && recentInvites is not null)
        {
            foreach (var c in recentInvites)
                if (message.Contains(c))
                {
                    attemptedWorkaround |= inviteCodes.Add(c);
                    InviteCodeCache.Set(author.Id, recentInvites, CacheDuration);
                }
        }
        if (inviteCodes.Count == 0 && discordMeLinks.Count == 0 && !tryMessageAsACode)
            return (false, attemptedWorkaround, new(0));

        var hasInvalidInvites = false;
        foreach (var meLink in discordMeLinks)
        {
            /*
             * discord.me is a fucking joke and so far they were unwilling to provide any kind of sane api
             * here's their current flow:
             * 1. get vanity page (e.g. https://discord.me/rpcs3)
             * 2. POST web form with csrf token and server EID to https://discord.me/server/join
             * 3. this will return a 302 redirect (Location header value) to https://discord.me/server/join/protected/_some_id_
             * 4. this page will have a "refresh" meta tag in its body to ttps://discord.me/server/join/redirect/_same_id_
             * 5. this one will return a 302 redirect to an actual https://discord.gg/_invite_id_
             */
            try
            {
                using var handler = new HttpClientHandler {AllowAutoRedirect = false}; // needed to store cloudflare session cookies
                using var httpClient = HttpClientFactory.Create(handler, new CompressionMessageHandler());
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.me/" + meLink);
                request.Headers.Accept.Add(new("text/html"));
                request.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                request.Headers.UserAgent.Add(new("RPCS3CompatibilityBot", "2.0"));
                using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrEmpty(html))
                        continue;

                    hasInvalidInvites = true;
                    var csrfTokenMatch = CsrfTokenPattern().Match(html);
                    var serverEidMatch = ServerEidPattern().Match(html);
                    if (csrfTokenMatch.Success && serverEidMatch.Success)
                    {
                        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "https://discord.me/server/join")
                        {
                            Content = new FormUrlEncodedContent(new Dictionary<string, string>
                            {
                                ["_token"] = csrfTokenMatch.Groups["csrf_token"].Value,
                                ["serverEid"] = serverEidMatch.Groups["server_eid"].Value,
                            }!),
                        };
                        postRequest.Headers.Accept.Add(new("text/html"));
                        postRequest.Headers.UserAgent.Add(new("RPCS3CompatibilityBot", "2.0"));
                        using var postResponse = await httpClient.SendAsync(postRequest).ConfigureAwait(false);
                        if (postResponse.StatusCode == HttpStatusCode.Redirect)
                        {
                            var redirectId = postResponse.Headers.Location?.Segments.Last();
                            if (redirectId != null)
                            {
                                using var getDiscordRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.me/server/join/redirect/" + redirectId);
                                getDiscordRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                                getDiscordRequest.Headers.UserAgent.Add(new("RPCS3CompatibilityBot", "2.0"));
                                using var discordRedirect = await httpClient.SendAsync(getDiscordRequest).ConfigureAwait(false);
                                if (discordRedirect.StatusCode == HttpStatusCode.Redirect)
                                {
                                    var inviteCodeSegment = discordRedirect.Headers.Location?.Segments.Last();
                                    if (inviteCodeSegment != null)
                                    {
                                        inviteCodes.Add(inviteCodeSegment);
                                        hasInvalidInvites = false;
                                    }
                                }
                                else
                                    Config.Log.Warn($"Unexpected response code from GET discord redirect: {discordRedirect.StatusCode}");
                            }
                            else
                                Config.Log.Warn($"Failed to get redirection URL from {postResponse.RequestMessage?.RequestUri}");
                        }
                        else
                            Config.Log.Warn($"Unexpected response code from POST: {postResponse.StatusCode}");
                    }
                    else
                        Config.Log.Warn($"Failed to get POST arguments from discord.me: {html}");
                }
                else
                    Config.Log.Warn($"Got {response.StatusCode} from discord.me: {html}");
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        if (tryMessageAsACode)
            inviteCodes.Add(message);

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
        return (hasInvalidInvites, attemptedWorkaround, result);
    }
}