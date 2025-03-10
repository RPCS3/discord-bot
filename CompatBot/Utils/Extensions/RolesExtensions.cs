using CompatBot.Database.Providers;

namespace CompatBot.Utils;

internal static class RolesExtensions
{
    public static async ValueTask<bool> IsModeratorAsync(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;
            
        if (ModProvider.IsSudoer(user.Id))
            return true;

        var member = await (guild == null ? client.GetMemberAsync(user) : client.GetMemberAsync(guild, user)).ConfigureAwait(false);
        return member?.Roles.IsModerator() is true;
    }

    public static async ValueTask<bool> IsWhitelistedAsync(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        if (ModProvider.IsMod(user.Id))
            return true;

        var member = await (guild == null ? client.GetMemberAsync(user) : client.GetMemberAsync(guild, user)).ConfigureAwait(false);
        return member?.Roles.IsWhitelisted() is true;
    }

    public static async ValueTask<bool> IsSmartlistedAsync(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        if (ModProvider.IsMod(user.Id))
            return true;

        var member = await (guild == null ? client.GetMemberAsync(user) : client.GetMemberAsync(guild, user)).ConfigureAwait(false);
        return member?.Roles.IsSmartlisted() is true;
    }

    public static async ValueTask<bool> IsSupporterAsync(this DiscordUser? user, DiscordClient client, DiscordGuild? guild = null)
    {
        if (user == null)
            return false;

        var member = await (guild == null ? client.GetMemberAsync(user) : client.GetMemberAsync(guild, user)).ConfigureAwait(false);
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