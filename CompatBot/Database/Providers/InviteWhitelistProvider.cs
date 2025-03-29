using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

internal static class InviteWhitelistProvider
{
    public static async ValueTask<bool> IsWhitelistedAsync(ulong guildId)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        return db.WhitelistedInvites.Any(i => i.GuildId == guildId);
    }

    public static async ValueTask<bool> IsWhitelistedAsync(DiscordInvite invite)
    {
        var code = string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
        var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
        WhitelistedInvite? savedInfo;
        await using (var db = await BotDb.OpenReadAsync().ConfigureAwait(false))
        {
            savedInfo = await db.WhitelistedInvites
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.GuildId == invite.Guild.Id)
                .ConfigureAwait(false);
            if (savedInfo is null)
                return false;

            if (savedInfo.InviteCode == code
                && (savedInfo.Name == name
                    || savedInfo.Name is not { Length: > 0 } && name is not { Length: > 0 }
                ))
                return true;
        }
        
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var whitelistedInvite = await wdb.WhitelistedInvites
            .FirstAsync(i => i.Id == savedInfo.Id)
            .ConfigureAwait(false);
        if (name is {Length: >0} && name != whitelistedInvite.Name)
            whitelistedInvite.Name = invite.Guild.Name;
        if (code is {Length: >0}
            && !invite.IsRevoked
            && (!invite.IsTemporary || whitelistedInvite.InviteCode is not { Length: > 0 }))
            whitelistedInvite.InviteCode = code;
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async ValueTask<bool> AddAsync(DiscordInvite invite)
    {
        if (await IsWhitelistedAsync(invite).ConfigureAwait(false))
            return false;

        var code = invite.IsRevoked || string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
        var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        await wdb.WhitelistedInvites.AddAsync(new() { GuildId = invite.Guild.Id, Name = name, InviteCode = code }).ConfigureAwait(false);
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async ValueTask<bool> AddAsync(ulong guildId)
    {
        if (await IsWhitelistedAsync(guildId).ConfigureAwait(false))
            return false;

        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        await wdb.WhitelistedInvites.AddAsync(new() {GuildId = guildId}).ConfigureAwait(false);
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async ValueTask<bool> RemoveAsync(int id)
    {
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var dbItem = await wdb.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
        if (dbItem is null)
            return false;

        wdb.WhitelistedInvites.Remove(dbItem);
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    public static async Task CleanupAsync(DiscordClient client)
    {
        while (!Config.Cts.IsCancellationRequested)
        {
            await using (var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false))
            {
                foreach (var invite in wdb.WhitelistedInvites.Where(i => i.InviteCode != null))
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
                await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            await Task.Delay(TimeSpan.FromHours(1), Config.Cts.Token).ConfigureAwait(false);
        }
    }
}