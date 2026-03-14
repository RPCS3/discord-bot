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

    extension(DiscordUser user)
    {
        public string ToSaltedSha256() => BitConverter.GetBytes(user.Id).GetSaltedHash().ToHexString();

        public string DisplayName => user.GlobalName ?? user.Username;

        public async ValueTask<bool> AddRoleAsync(ulong roleId, DiscordClient client, DiscordGuild? guild, string reason)
        {
            if (await client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember member
                && !member.Roles.Any(r => r.Id == roleId)
                && await client.FindRoleAsync(guild, roleId).ConfigureAwait(false) is DiscordRole warnRole)
                try
                {
                    await member.GrantRoleAsync(warnRole, reason).ConfigureAwait(false);
                    return true;
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Failed to assign warning role to user {user.Username} ({user.Id})");
                }
            return false;
        }

        public async ValueTask<bool> RemoveRoleAsync(ulong roleId, DiscordClient client, DiscordGuild guild, string reason)
        {
            if (await client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember member)
                return await member.RemoveRoleAsync(roleId, client, guild, reason).ConfigureAwait(false);
            return false;
        }
    }

    public static async ValueTask<bool> RemoveRoleAsync(this DiscordMember member, ulong roleId, DiscordClient client, DiscordGuild guild, string reason)
    {
        if (roleId > 0
            && member.Roles.FirstOrDefault(r => r.Id == roleId) is DiscordRole role)
            try
            {
                await member.RevokeRoleAsync(role, reason).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to revoke warning role from user {member.DisplayName} ({member.Username}; {member.Id})");
            }
        return false;

    }
}