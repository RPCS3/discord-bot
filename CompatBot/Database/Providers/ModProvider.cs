﻿using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

internal static class ModProvider
{
    private static readonly Dictionary<ulong, Moderator> Moderators;
    public static ReadOnlyDictionary<ulong, Moderator> Mods => new(Moderators);

    static ModProvider()
    {
        using var db = BotDb.OpenRead();
        Moderators = db.Moderator.AsNoTracking().ToDictionary(m => m.DiscordId, m => m);
    }

    public static bool IsMod(ulong userId) => Moderators.ContainsKey(userId);
    public static bool IsSudoer(ulong userId) => Moderators.TryGetValue(userId, out var mod) && mod.Sudoer;

    public static async ValueTask<bool> AddAsync(ulong userId)
    {
        if (IsMod(userId))
            return false;

        var newMod = new Moderator {DiscordId = userId};
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        await wdb.Moderator.AddAsync(newMod).ConfigureAwait(false);
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        lock (Moderators)
        {
            if (IsMod(userId))
                return false;
            
            Moderators[userId] = newMod;
        }
        return true;
    }

    public static async ValueTask<bool> RemoveAsync(ulong userId)
    {
        if (!Moderators.ContainsKey(userId))
            return false;

        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var mod = await wdb.Moderator.FirstOrDefaultAsync(m => m.DiscordId == userId).ConfigureAwait(false);
        if (mod is not null)
        {
            wdb.Moderator.Remove(mod);
            await wdb.SaveChangesAsync().ConfigureAwait(false);
        }
        lock (Moderators)
        {
            if (IsMod(userId))
                Moderators.Remove(userId);
            else
                return false;
        }
        return true;
    }

    public static async ValueTask<bool> MakeSudoerAsync(ulong userId)
    {
        if (!Moderators.TryGetValue(userId, out var mod) || mod.Sudoer)
            return false;

        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var dbMod = await wdb.Moderator.FirstOrDefaultAsync(m => m.DiscordId == userId).ConfigureAwait(false);
        if (dbMod is not null)
        {
            dbMod.Sudoer = true;
            await wdb.SaveChangesAsync().ConfigureAwait(false);
        }
        mod.Sudoer = true;
        return true;
    }

    public static async ValueTask<bool> UnmakeSudoerAsync(ulong userId)
    {
        if (!Moderators.TryGetValue(userId, out var mod) || !mod.Sudoer)
            return false;

        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var dbMod = await wdb.Moderator.FirstOrDefaultAsync(m => m.DiscordId == userId).ConfigureAwait(false);
        if (dbMod is not null)
        {
            dbMod.Sudoer = false;
            await wdb.SaveChangesAsync().ConfigureAwait(false);
        }
        mod.Sudoer = false;
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        return true;
    }

    [Obsolete]
    public static async Task SyncRolesAsync(DiscordGuild guild)
    {
        Config.Log.Debug("Syncing moderator list to the sudoer role");
        var modRoleList = guild.Roles.Where(kvp => kvp.Value.Name.Equals("Moderator")).ToList();
        if (modRoleList.Count is 0)
            return;

        var modRole = modRoleList.First().Value;
        var members = guild.GetAllMembersAsync().ToList();
        var guildMods = members.Where(m => m.Roles.Any(r => r.Id == modRole.Id) && !m.IsBot && !m.IsCurrent).ToList();
        foreach (var mod in guildMods)
        {
            if (!IsMod(mod.Id))
            {
                Config.Log.Debug($"Making {mod.Username}#{mod.Discriminator} a bot mod");
                await AddAsync(mod.Id).ConfigureAwait(false);
            }
            if (!IsSudoer(mod.Id))
            {
                Config.Log.Debug($"Making {mod.Username}#{mod.Discriminator} a bot sudoer");
                await MakeSudoerAsync(mod.Id).ConfigureAwait(false);
            }
        }
    }
}