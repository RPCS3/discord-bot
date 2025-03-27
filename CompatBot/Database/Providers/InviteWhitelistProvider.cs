using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

internal static class InviteWhitelistProvider
{
    public static bool IsWhitelisted(ulong guildId)
    {
        using var db = BotDb.OpenRead();
        return db.WhitelistedInvites.Any(i => i.GuildId == guildId);
    }

    public static async Task<bool> IsWhitelistedAsync(DiscordInvite invite)
    {
        var code = string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
        var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
        await using var db = BotDb.OpenWrite();
        var whitelistedInvite = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.GuildId == invite.Guild.Id);
        if (whitelistedInvite is null)
            return false;

        if (name != null && name != whitelistedInvite.Name)
            whitelistedInvite.Name = invite.Guild.Name;
        if (string.IsNullOrEmpty(whitelistedInvite.InviteCode) && code != null)
            whitelistedInvite.InviteCode = code;
        await db.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async Task<bool> AddAsync(DiscordInvite invite)
    {
        if (await IsWhitelistedAsync(invite).ConfigureAwait(false))
            return false;

        var code = invite.IsRevoked || string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
        var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
        await using var db = BotDb.OpenWrite();
        await db.WhitelistedInvites.AddAsync(new WhitelistedInvite { GuildId = invite.Guild.Id, Name = name, InviteCode = code }).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async Task<bool> AddAsync(ulong guildId)
    {
        if (IsWhitelisted(guildId))
            return false;

        await using var db = BotDb.OpenWrite();
        await db.WhitelistedInvites.AddAsync(new WhitelistedInvite {GuildId = guildId}).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async Task<bool> RemoveAsync(int id)
    {
        await using var db = BotDb.OpenWrite();
        var dbItem = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
        if (dbItem == null)
            return false;

        db.WhitelistedInvites.Remove(dbItem);
        await db.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async Task CleanupAsync(DiscordClient client)
    {
        while (!Config.Cts.IsCancellationRequested)
        {
            await using var db = BotDb.OpenWrite();
            foreach (var invite in db.WhitelistedInvites.Where(i => i.InviteCode != null))
            {
                try
                {
                    var result = await client.GetInviteByCodeAsync(invite.InviteCode).ConfigureAwait(false);
                    if (result.IsRevoked)
                        invite.InviteCode = null;
                }
                catch (NotFoundException)
                {
                    invite.InviteCode = null;
                    Config.Log.Info($"Removed invite code {invite.InviteCode} for server {invite.Name}");
                }
                catch (Exception e)
                {
                    Config.Log.Debug(e);
                }
            }
            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromHours(1), Config.Cts.Token).ConfigureAwait(false);
        }
    }
}