using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using HomoglyphConverter;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.EventHandlers;

internal static class UsernameSpoofMonitor
{
    private static readonly Dictionary<string, string> UsernameMapping = new();
    private static readonly SemaphoreSlim UsernameLock = new(1, 1);
    private static readonly MemoryCache SpoofingReportThrottlingCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(10) });
    private static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(1);

    public static async Task OnUserUpdated(DiscordClient c, UserUpdateEventArgs args)
    {
        if (args.UserBefore.Username == args.UserAfter.Username)
            return;

        var m = await c.GetMemberAsync(args.UserAfter).ConfigureAwait(false);
        if (m is null)
            return;
            
        var potentialTargets = GetPotentialVictims(c, m, true, false);
        if (!potentialTargets.Any())
            return;

        if (await IsFlashMobAsync(c, potentialTargets).ConfigureAwait(false))
            return;

        await c.ReportAsync("🕵️ Potential user impersonation",
            $"User {m.GetMentionWithNickname()} has changed their __username__ from " +
            $"**{args.UserBefore.Username.Sanitize()}#{args.UserBefore.Discriminator}** to " +
            $"**{args.UserAfter.Username.Sanitize()}#{args.UserAfter.Discriminator}**",
            potentialTargets,
            ReportSeverity.Medium);
    }

    public static async Task OnMemberUpdated(DiscordClient c, GuildMemberUpdateEventArgs args)
    {
        if (args.NicknameBefore == args.NicknameAfter)
            return;

        var potentialTargets = GetPotentialVictims(c, args.Member, false, true);
        if (!potentialTargets.Any())
            return;

        if (await IsFlashMobAsync(c, potentialTargets).ConfigureAwait(false))
            return;

        await c.ReportAsync("🕵️ Potential user impersonation",
            $"Member {args.Member.GetMentionWithNickname()} has changed their __display name__ from " +
            $"**{(args.NicknameBefore ?? args.Member.Username).Sanitize()}** to " +
            $"**{args.Member.DisplayName.Sanitize()}**",
            potentialTargets,
            ReportSeverity.Medium);
    }

    public static async Task OnMemberAdded(DiscordClient c, GuildMemberAddEventArgs args)
    {
        var potentialTargets = GetPotentialVictims(c, args.Member, true, true);
        if (!potentialTargets.Any())
            return;

        if (await IsFlashMobAsync(c, potentialTargets).ConfigureAwait(false))
            return;

        await c.ReportAsync("🕵️ Potential user impersonation",
            $"New member joined the server: {args.Member.GetMentionWithNickname()}",
            potentialTargets,
            ReportSeverity.Medium);
    }

    internal static List<DiscordMember> GetPotentialVictims(DiscordClient client, DiscordMember newMember, bool checkUsername, bool checkNickname, List<DiscordMember>? listToCheckAgainst = null)
    {
        var membersWithRoles = listToCheckAgainst ??
                               client.Guilds.SelectMany(guild => guild.Value.Members.Values)
                                   .Where(m => m.Roles.Any() || m.IsCurrent)
                                   .OrderByDescending(m => m.Hierarchy)
                                   .ThenByDescending(m => m.JoinedAt)
                                   .ToList();
        var newUsername = GetCanonical(newMember.Username);
        var newDisplayName = GetCanonical(newMember.DisplayName);
        var potentialTargets = new List<DiscordMember>();
        foreach (var memberWithRole in membersWithRoles)
            if (checkUsername && newUsername == GetCanonical(memberWithRole.Username) && newMember.Id != memberWithRole.Id)
                potentialTargets.Add(memberWithRole);
            else if (checkNickname && (newDisplayName == GetCanonical(memberWithRole.DisplayName) || newDisplayName == GetCanonical(memberWithRole.Username)) && newMember.Id != memberWithRole.Id)
                potentialTargets.Add(memberWithRole);
        return potentialTargets;
    }

    private static async Task<bool> IsFlashMobAsync(DiscordClient client, List<DiscordMember> potentialVictims)
    {
        if (potentialVictims.Count == 0)
            return false;
            
        try
        {
            var displayName = GetCanonical(potentialVictims[0].DisplayName);
            if (SpoofingReportThrottlingCache.TryGetValue(displayName, out string? s) && !string.IsNullOrEmpty(s))
            {
                SpoofingReportThrottlingCache.Set(displayName, s, SnoozeDuration);
                return true;
            }

            if (potentialVictims.Count > 3)
            {
                SpoofingReportThrottlingCache.Set(displayName, "y", SnoozeDuration);
                var channel = await client.GetChannelAsync(Config.BotLogId).ConfigureAwait(false);
                await channel.SendMessageAsync($"`{displayName.Sanitize()}` is a popular member! Snoozing notifications for an hour").ConfigureAwait(false);
                return true;
            }
        }
        catch (Exception e)
        {
            Config.Log.Debug(e);
        }
        return false;
    }

    private static string GetCanonical(string name)
    {
        if (UsernameLock.Wait(0))
            try
            {
                if (UsernameMapping.TryGetValue(name, out var result))
                    return result;
            }
            finally
            {
                UsernameLock.Release();
            }
        var canonicalName = name.ToCanonicalForm();
        if (UsernameLock.Wait(0))
            try
            {
                UsernameMapping[name] = canonicalName;
            }
            finally
            {
                UsernameLock.Release();
            }
        return canonicalName;
    }
}