using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using HomoglyphConverter;
using Microsoft.Extensions.Caching.Memory;
using ResultNet;

namespace CompatBot.EventHandlers;

internal static partial class DiscordInviteFilter
{
    [GeneratedRegex(@"\bdiscord((((app)?\.com/invite|\.gg)/(?<invite_id>[a-z0-9\-]+))|(\.me/(?<me_id>.*?))(\s|>|$))", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline)]
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
        if (message.Channel is null or { IsPrivate: true })
            return true;

        if (message.Author is null or { IsBot: true })
            return true;

#if !DEBUG
        if (await message.Author.IsWhitelistedAsync(client, message.Channel.Guild).ConfigureAwait(false))
            return true;
#endif

        if (message.Reactions.Any(r => r.Emoji == Config.Reactions.Moderated && r.IsMe))
            return true;

        var messageContent = await message.GetMessageContentForFiltersAsync(client, false, false).ConfigureAwait(false);
        var (hasInvalidResults, attemptedWorkaround, invites) = await client.GetInvitesAsync(messageContent, message.Author).ConfigureAwait(false);
        if (!hasInvalidResults && invites is [])
            return true;

        if (hasInvalidResults)
        {
            try
            {
                DeletedMessagesMonitor.RemovedByBotCache.Set(message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                await message.DeleteAsync("Not a white-listed discord invite link").ConfigureAwait(false);
                await client.ReportAsync("🛃 An unapproved discord invite", message, "An invalid or expired invite",  null, null, null, ReportSeverity.Low).ConfigureAwait(false);
                await message.Channel.SendMessageAsync($"{message.Author.Mention} please refrain from posting invites that were not approved by a moderator, especially expired or invalid.").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                await client.ReportAsync("🛃 An unapproved discord invite", message, "An invalid or expired invite", null, null, null, ReportSeverity.Medium).ConfigureAwait(false);
                await message.ReactWithAsync(Config.Reactions.Moderated,
                    $"{message.Author.Mention} please remove this expired or invalid invite, and refrain from posting it again until you have received an approval from a moderator.",
                    true
                ).ConfigureAwait(false);
            }
            return false;
        }

        Piracystring? match = null;
        var kicked = false;
        foreach (var invite in invites)
        {
            if (!await InviteWhitelistProvider.IsWhitelistedAsync(invite).ConfigureAwait(false))
            {
                if (!InviteCodeCache.TryGetValue(message.Author.Id, out HashSet<string>? recentInvites) || recentInvites is null)
                    recentInvites = [];
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

                var guildName = $"""
                                 {invite.Guild.Name}
                                 {invite.Guild.Name.ToCanonicalForm()}
                                 """;
                if (await ContentFilter.FindTriggerAsync(FilterContext.Invite, guildName).ConfigureAwait(false) is Piracystring trigger
                    && trigger.Actions.HasFlag(FilterAction.Kick)
                    && ! kicked)
                {
                    match ??= trigger;
                    try
                    {
                        if (await client.GetMemberAsync(message.Channel.Guild, message.Author).ConfigureAwait(false) is DiscordMember member)
                        {
                            await member.RemoveAsync($"Invite filter #{trigger.Id}");
                            kicked = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to kick user {message.Author.DisplayName} ({message.Author.Id})");
                    }
                }
                
                var codeResolveMsg = $"Invite `{invite.Code}` was resolved to the `{invite.Guild.Name.Sanitize()}` server";
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
                string? actions = null;
                if (removed)
                    actions += $"✅ {FilterAction.RemoveContent}";
                if (match is not null)
                {
                    if (kicked)
                        actions += $"✅ {FilterAction.Kick}";
                    else
                        actions += $"❌ {FilterAction.Kick}";
                }
                if (!kicked && (repeatedInvitePost || recentInvites.Count > 1))
                    try
                    {
                        var member = await client.GetMemberAsync(message.Channel.Guild, message.Author).ConfigureAwait(false);
                        if (member is not null)
                        {
                            await member.RemoveAsync("Multiple invites after being warned").ConfigureAwait(false);
                            kicked = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to kick user {await message.Author.GetUsernameWithNicknameAsync(client).ConfigureAwait(false)} for repeated invite spam");
                    }
                await client.ReportAsync(
                    "🛃 An unapproved discord invite",
                    message,
                    invite.Code,
                    match?.String,
                    match?.Id,
                    reportMsg,
                    ReportSeverity.Low,
                    actions,
                    quoteContext: false
                ).ConfigureAwait(false);
                if (kicked)
                    return false;
                
                await message.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
                if (circumventionAttempt)
                {
                    var reason = "Attempted to circumvent discord invite filter";
                    var result = await Warnings.AddAsync(
                        message.Author.Id,
                        client.CurrentUser,
                        reason,
                        codeResolveMsg,
                        true
                    ).ConfigureAwait(false);
                    await message.Author.AddRoleAsync(Config.WarnRoleId, client, message.Channel?.Guild, reason).ConfigureAwait(false);
                    if (result.IsSuccess() && !result.Data.suppress)
                    {
                        var (_, recent, total, _) = result.Data;
                        var content = await Warnings.GetDefaultWarningMessageAsync(client, message.Author, reason, recent, total, client.CurrentUser).ConfigureAwait(false);
                        var msg = new DiscordMessageBuilder()
                            .WithContent(content)
                            .AddMention(new UserMention(message.Author));
                        await message.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                    }
                }
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
            if (botMember is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                botMember = await client.GetMemberAsync(guild, client.CurrentUser).ConfigureAwait(false);
                if (botMember is null)
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
        if (message is not { Length: >0 })
            return (false, false, []);

        var inviteCodes = new HashSet<string>(InviteLink().Matches(message).Select(m => m.Groups["invite_id"].Value).Where(s => s is { Length: >0 }));
        var discordMeLinks = InviteLink().Matches(message).Select(m => m.Groups["me_id"].Value).Distinct().Where(s => s is { Length: >0 }).ToList();
        var attemptedWorkaround = false;
        if (author is not null && InviteCodeCache.TryGetValue(author.Id, out HashSet<string>? recentInvites) && recentInvites is not null)
        {
            foreach (var c in recentInvites)
                if (message.Contains(c))
                {
                    attemptedWorkaround |= inviteCodes.Add(c);
                    InviteCodeCache.Set(author.Id, recentInvites, CacheDuration);
                }
        }
        if (inviteCodes is not { Count: >0 } && discordMeLinks is [] && !tryMessageAsACode)
            return (false, attemptedWorkaround, []);

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
                using var httpClient = HttpClientFactory.Create(handler, new CompressionMessageHandler()).WithUserAgent();
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.me/" + meLink);
                request.Headers.Accept.Add(new("text/html"));
                request.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
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
                            }),
                        };
                        postRequest.Headers.Accept.Add(new("text/html"));
                        using var postResponse = await httpClient.SendAsync(postRequest).ConfigureAwait(false);
                        if (postResponse.StatusCode == HttpStatusCode.Redirect)
                        {
                            if (postResponse.Headers.Location?.Segments.Last() is {Length: >0} redirectId)
                            {
                                using var getDiscordRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.me/server/join/redirect/" + redirectId);
                                getDiscordRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
                                using var discordRedirect = await httpClient.SendAsync(getDiscordRequest).ConfigureAwait(false);
                                if (discordRedirect.StatusCode == HttpStatusCode.Redirect)
                                {
                                    var inviteCodeSegment = discordRedirect.Headers.Location?.Segments.Last();
                                    if (inviteCodeSegment is not null)
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