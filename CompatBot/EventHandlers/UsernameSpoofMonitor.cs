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

namespace CompatBot.EventHandlers
{
    internal static class UsernameSpoofMonitor
    {
        private static readonly Dictionary<string, string> UsernameMapping = new Dictionary<string, string>();
        private static readonly SemaphoreSlim UsernameLock = new SemaphoreSlim(1, 1);
        private static readonly MemoryCache SpoofingReportThrottlingCache = new MemoryCache(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(10) });
        private static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(1);

        public static async Task OnUserUpdated(UserUpdateEventArgs args)
        {
            if (args.UserBefore.Username == args.UserAfter.Username)
                return;

            var potentialTargets = GetPotentialVictims(args.Client, args.Client.GetMember(args.UserAfter), true, false);
            if (!potentialTargets.Any())
                return;

            if (await IsFlashmobAsync(args.Client, potentialTargets).ConfigureAwait(false))
                return;

            var m = args.Client.GetMember(args.UserAfter);
            await args.Client.ReportAsync("Potential user impersonation",
                $"User {m.GetMentionWithNickname()} has changed their __username__ from " +
                $"**{args.UserBefore.Username.Sanitize()}#{args.UserBefore.Discriminator}** to " +
                $"**{args.UserAfter.Username.Sanitize()}#{args.UserAfter.Discriminator}**",
                potentialTargets,
                ReportSeverity.Medium);
        }

        public static async Task OnMemberUpdated(GuildMemberUpdateEventArgs args)
        {
            if (args.NicknameBefore == args.NicknameAfter)
                return;

            var potentialTargets = GetPotentialVictims(args.Client, args.Member, false, true);
            if (!potentialTargets.Any())
                return;

            if (await IsFlashmobAsync(args.Client, potentialTargets).ConfigureAwait(false))
                return;

            await args.Client.ReportAsync("Potential user impersonation",
                $"Member {args.Member.GetMentionWithNickname()} has changed their __display name__ from " +
                $"**{(args.NicknameBefore ?? args.Member.Username).Sanitize()}** to " +
                $"**{args.Member.DisplayName.Sanitize()}**",
                potentialTargets,
                ReportSeverity.Medium);
        }

        public static async Task OnMemberAdded(GuildMemberAddEventArgs args)
        {
            var potentialTargets = GetPotentialVictims(args.Client, args.Member, true, true);
            if (!potentialTargets.Any())
                return;

            if (await IsFlashmobAsync(args.Client, potentialTargets).ConfigureAwait(false))
                return;

            await args.Client.ReportAsync("Potential user impersonation",
                $"New member joined the server: {args.Member.GetMentionWithNickname()}",
                potentialTargets,
                ReportSeverity.Medium);
        }

        internal static List<DiscordMember> GetPotentialVictims(DiscordClient client, DiscordMember newMember, bool checkUsername, bool checkNickname, List<DiscordMember> listToCheckAgainst = null)
        {
            var membersWithRoles = listToCheckAgainst ??
                                   client.Guilds.SelectMany(guild => guild.Value.Members)
                                       .Where(m => m.Roles.Any())
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

        private static async Task<bool> IsFlashmobAsync(DiscordClient client, List<DiscordMember> potentialVictims)
        {
            if (potentialVictims?.Count > 0)
                try
                {
                    var displayName = GetCanonical(potentialVictims[0].DisplayName);
                    if (SpoofingReportThrottlingCache.TryGetValue(displayName, out string s) && !string.IsNullOrEmpty(s))
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
            string result;
            if (UsernameLock.Wait(0))
                try
                {
                    if (UsernameMapping.TryGetValue(name, out result))
                        return result;
                }
                finally
                {
                    UsernameLock.Release(1);
                }
            result = Normalizer.ToCanonicalForm(name);
            if (UsernameLock.Wait(0))
                try
                {
                    UsernameMapping[name] = result;
                }
                finally
                {
                    UsernameLock.Release(1);
                }
            return result;
        }
    }
}
