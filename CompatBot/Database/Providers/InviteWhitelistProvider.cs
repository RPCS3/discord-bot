using System.Linq;
using System.Threading.Tasks;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers
{
    internal static class InviteWhitelistProvider
    {
        public static bool IsWhitelisted(ulong guildId)
        {
            using (var db = new BotDb())
                return db.WhitelistedInvites.Any(i => i.GuildId == guildId);
        }

        public static async Task<bool> IsWhitelistedAsync(DiscordInvite invite)
        {
            var code = invite.IsTemporary || string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
            var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
            using (var db = new BotDb())
            {
                var whitelistedInvite = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.GuildId == invite.Guild.Id);
                if (whitelistedInvite == null)
                    return false;

                if (name != null && name != whitelistedInvite.Name)
                    whitelistedInvite.Name = invite.Guild.Name;
                if (code != null && code != whitelistedInvite.InviteCode)
                    whitelistedInvite.InviteCode = code;
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public static async Task<bool> AddAsync(DiscordInvite invite)
        {
            if (await IsWhitelistedAsync(invite).ConfigureAwait(false))
                return false;

            var code = invite.IsTemporary || string.IsNullOrWhiteSpace(invite.Code) ? null : invite.Code;
            var name = string.IsNullOrWhiteSpace(invite.Guild.Name) ? null : invite.Guild.Name;
            using (var db = new BotDb())
            {
                await db.WhitelistedInvites.AddAsync(new WhitelistedInvite { GuildId = invite.Guild.Id, Name = name, InviteCode = code }).ConfigureAwait(false);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return true;
        }

        public static async Task<bool> AddAsync(ulong guildId)
        {
            if (IsWhitelisted(guildId))
                return false;

            using (var db = new BotDb())
            {
                await db.WhitelistedInvites.AddAsync(new WhitelistedInvite {GuildId = guildId}).ConfigureAwait(false);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return true;
        }

        public static async Task<bool> RemoveAsync(int id)
        {
            using (var db = new BotDb())
            {
                var dbItem = await db.WhitelistedInvites.FirstOrDefaultAsync(i => i.Id == id).ConfigureAwait(false);
                if (dbItem == null)
                    return false;

                db.WhitelistedInvites.Remove(dbItem);
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }
    }
}