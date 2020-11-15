using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace CompatBot.Database.Providers
{
    internal static class ModProvider
    {
        private static readonly Dictionary<ulong, Moderator> Moderators;
        private static readonly BotDb Db = new();
        public static ReadOnlyDictionary<ulong, Moderator> Mods => new(Moderators);

        static ModProvider()
        {
            Moderators = Db.Moderator.ToDictionary(m => m.DiscordId, m => m);
        }

        public static bool IsMod(ulong userId) => Moderators.ContainsKey(userId);

        public static bool IsSudoer(ulong userId) => Moderators.TryGetValue(userId, out var mod) && mod.Sudoer;

        public static async Task<bool> AddAsync(ulong userId)
        {
            if (IsMod(userId))
                return false;

            var result = await Db.Moderator.AddAsync(new Moderator {DiscordId = userId}).ConfigureAwait(false);
            await Db.SaveChangesAsync().ConfigureAwait(false);
            lock (Moderators)
            {
                if (IsMod(userId))
                    return false;
                Moderators[userId] = result.Entity;
            }
            return true;
        }

        public static async Task<bool> RemoveAsync(ulong userId)
        {
            if (!Moderators.TryGetValue(userId, out var mod))
                return false;

            Db.Moderator.Remove(mod);
            await Db.SaveChangesAsync().ConfigureAwait(false);
            lock (Moderators)
            {
                if (IsMod(userId))
                    Moderators.Remove(userId);
                else
                    return false;
            }
            return true;
        }

        public static async Task<bool> MakeSudoerAsync(ulong userId)
        {
            if (!Moderators.TryGetValue(userId, out var mod) || mod.Sudoer)
                return false;

            mod.Sudoer = true;
            await Db.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }

        public static async Task<bool> UnmakeSudoerAsync(ulong userId)
        {
            if (!Moderators.TryGetValue(userId, out var mod) || !mod.Sudoer)
                return false;

            mod.Sudoer = false;
            await Db.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }

        public static async Task SyncRolesAsync(DiscordGuild guild)
        {
            Config.Log.Debug("Syncing moderator list to the sudoer role");
            var modRoleList = guild.Roles.Where(kvp => kvp.Value.Name.Equals("Moderator")).ToList();
            if (modRoleList.Count == 0)
                return;

            var modRole = modRoleList.First().Value;
            var members = await guild.GetAllMembersAsync().ConfigureAwait(false);
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
}
