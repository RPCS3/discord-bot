using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers
{
    public static class UsernameValidationMonitor
    {
        public static async Task OnMemberUpdated(GuildMemberUpdateEventArgs args)
        {
            await UpdateDisplayName(args.Guild.CurrentMember, args.Member).ConfigureAwait(false);
        }   

        public static async Task OnMemberAdded(GuildMemberAddEventArgs args)
        {
            await UpdateDisplayName(args.Guild.CurrentMember, args.Member).ConfigureAwait(false);
        }

        private static async Task UpdateDisplayName(DiscordMember bot, DiscordMember guildMember)
        {
            try
            {
                if (guildMember.IsWhitelisted())
                    return;

                using (var context = new BotDb())
                {
                    var forcedNickname = await context.ForcedNicknames.FirstOrDefaultAsync(x => x.UserId == guildMember.Id && x.GuildId == guildMember.Guild.Id).ConfigureAwait(false);
                    if (forcedNickname is null)
                        return;
                    
                    if (guildMember.DisplayName == forcedNickname.Nickname)
                        return;

                    await guildMember.ModifyAsync(x => x.Nickname = forcedNickname.Nickname).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task MonitorAsync(DiscordClient client)
        {
            while (!Config.Cts.IsCancellationRequested)
            {
                await UpdateMembersNickname(client);
                await Task.Delay(TimeSpan.FromSeconds(Config.ForcedNicknamesRecheckTimeInSeconds), Config.Cts.Token).ConfigureAwait(false);
            }
        }

        public static async Task UpdateMembersNickname(DiscordClient client)
        {
            foreach (var (guildId, guild) in client.Guilds)
            {
                try
                {
                    using var context = new BotDb();
                    var forcedNicknames = await context.ForcedNicknames
                        .Where(x => x.GuildId == guildId)
                        .ToDictionaryAsync(x => x.UserId)
                        .ConfigureAwait(false);
                    var allMembers = await guild.GetAllMembersAsync().ConfigureAwait(false);
                    var membersToUpdate = allMembers.Where(x => forcedNicknames.TryGetValue(x.Id, out var forcedNickname)
                                                                && forcedNickname.Nickname != x.DisplayName)
                        .ToList();

                    foreach (var member in membersToUpdate)
                    {
                        await member.ModifyAsync(x => x.Nickname = forcedNicknames[member.Id].Nickname).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e);
                }
            }
        }
    }
}