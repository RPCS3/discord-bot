using System.Collections.Generic;
using System.Linq;
using CompatBot.Database.Providers;
using DSharpPlus;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    internal static class RolesExtensions
    {
        public static bool IsWhitelisted(this DiscordUser user, DiscordClient client, DiscordGuild guild = null)
        {
            if (user == null)
                return false;

            if (ModProvider.IsMod(user.Id))
                return true;

            if (client == null)
                return false;

            var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
            return member?.Roles.IsWhitelisted() ?? false;
        }

        public static bool IsSmartlisted(this DiscordUser user, DiscordClient client, DiscordGuild guild = null)
        {
            if (user == null)
                return false;

            if (ModProvider.IsMod(user.Id))
                return true;

            if (client == null)
                return false;

            var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
            return member?.Roles.IsSmartlisted() ?? false;
        }

        public static bool IsWhitelisted(this DiscordMember member)
        {
            return ModProvider.IsMod(member.Id) || member.Roles.IsWhitelisted();
        }

        public static bool IsSmartlisted(this DiscordMember member)
        {
            return ModProvider.IsMod(member.Id) || member.Roles.IsSmartlisted();
        }

        public static bool IsWhitelisted(this IEnumerable<DiscordRole> memberRoles)
        {
            return memberRoles?.Any(r => Config.Moderation.RoleWhiteList.Contains(r.Name)) ?? false;
        }

        public static bool IsSmartlisted(this IEnumerable<DiscordRole> memberRoles)
        {
            return memberRoles?.Any(r => Config.Moderation.RoleSmartList.Contains(r.Name)) ?? false;
        }
    }
}