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
            if (ModProvider.IsMod(user.Id))
                return true;

            var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
            return member?.Roles.IsWhitelisted() ?? false;
        }

        public static bool IsWhitelisted(this DiscordMember member)
        {
            return ModProvider.IsMod(member.Id) || member.Roles.IsWhitelisted();
        }

        public static bool IsWhitelisted(this IEnumerable<DiscordRole> memberRoles)
        {
            return memberRoles?.Any(r => Config.Moderation.RoleWhiteList.Contains(r.Name)) ?? false;
        }
    }
}