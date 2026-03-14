using PsnClient.Utils;

namespace CompatBot.Utils.Extensions;

public static class DiscordUserExtensions
{
    public static bool IsBotSafeCheck(this DiscordUser? user)
    {
        try
        {
            return user?.IsBot ?? false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
            return false;
        }
    }

    public static string ToSaltedSha256(this DiscordUser user)
        => BitConverter.GetBytes(user.Id).GetSaltedHash().ToHexString();

    public static async ValueTask<bool> AddRoleAsync(this DiscordUser user, ulong roleId, DiscordClient client, DiscordGuild guild, string reason)
    {
        if (roleId > 0
            && await client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember member
            && !member.Roles.Any(r => r.Id == Config.WarnRoleId))
            try
            {
                var warnRole = await guild.GetRoleAsync(Config.WarnRoleId).ConfigureAwait(false);
                await member.GrantRoleAsync(warnRole, reason).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to assign warning role to user {user.Username} ({user.Id})");
            }
        return false;
    }

    public static async ValueTask<bool> RemoveRoleAsync(this DiscordUser user, ulong roleId, DiscordClient client, DiscordGuild guild, string reason)
    {
        if (await client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember member)
            return await member.RemoveRoleAsync(roleId, client, guild, reason).ConfigureAwait(false);
        return false;
    }

    public static async ValueTask<bool> RemoveRoleAsync(this DiscordMember member, ulong roleId, DiscordClient client, DiscordGuild guild, string reason)
    {
        if (roleId > 0
            && member.Roles.FirstOrDefault(r => r.Id == Config.WarnRoleId) is DiscordRole role)
            try
            {
                await member.RevokeRoleAsync(role, reason).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to revoke warning role from user {member.Nickname} ({member.Username}; {member.Id})");
            }
        return false;

    }
}