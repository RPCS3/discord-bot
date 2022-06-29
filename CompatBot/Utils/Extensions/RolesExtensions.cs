using System.Collections.Generic;
using System.Linq;
using CompatBot.Database.Providers;
using DSharpPlus;
using DSharpPlus.Entities;

namespace CompatBot.Utils;

internal static class RolesExtensions
{
    public static bool IsModerator(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;
            
        if (ModProvider.IsSudoer(user.Id))
            return true;

        var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
        return member?.Roles.IsModerator() is true;
    }

    public static bool IsWhitelisted(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        if (ModProvider.IsMod(user.Id))
            return true;

        var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
        return member?.Roles.IsWhitelisted() is true;
    }

    public static bool IsSmartlisted(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        if (ModProvider.IsMod(user.Id))
            return true;

        var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
        return member?.Roles.IsSmartlisted() is true;
    }

    public static bool IsSupporter(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        var member = guild == null ? client.GetMember(user) : client.GetMember(guild, user);
        return member?.Roles.IsSupporter() is true;
    }

    public static bool IsWhitelisted(this DiscordMember member)
        => ModProvider.IsMod(member.Id) || member.Roles.IsWhitelisted();

    public static bool IsSmartlisted(this DiscordMember member)
        => ModProvider.IsMod(member.Id) || member.Roles.IsSmartlisted();

    public static bool IsModerator(this IEnumerable<DiscordRole> memberRoles)
        => memberRoles.Any(r => r.Name.Equals("Moderator"));

    public static bool IsWhitelisted(this IEnumerable<DiscordRole> memberRoles)
        => memberRoles.Any(r => Config.Moderation.RoleWhiteList.Contains(r.Name));

    public static bool IsSmartlisted(this IEnumerable<DiscordRole> memberRoles)
        => memberRoles.Any(r => Config.Moderation.RoleSmartList.Contains(r.Name));

    public static bool IsSupporter(this IEnumerable<DiscordRole> memberRoles)
        => memberRoles.Any(r => Config.Moderation.SupporterRoleList.Contains(r.Name));
}