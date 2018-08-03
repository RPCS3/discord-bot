using System.Collections.Generic;
using System.Linq;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    internal static class RolesExtensions
    {
        public static bool IsWhitelisted(this IEnumerable<DiscordRole> memberRoles)
        {
            return memberRoles?.Any(r => Config.Moderation.RoleWhiteList.Contains(r.Name)) ?? false;
        }
    }
}