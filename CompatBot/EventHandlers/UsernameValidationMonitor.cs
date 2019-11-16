using System;
using System.Collections.Generic;
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

        private static readonly IList<DiscordGuild> AvailableGuilds = new List<DiscordGuild>();

        public static void SetupGuilds(IEnumerable<DiscordGuild>guilds)
        {
            foreach (var discordGuild in guilds)
            {
                AvailableGuilds.Add(discordGuild);
            }
        }

        public static async Task MonitorAsync()
        {
            while (!Config.Cts.IsCancellationRequested)
            {
                if (!AvailableGuilds.Any())
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), Config.Cts.Token).ConfigureAwait(false);
                    continue;
                }
                foreach (var guild in AvailableGuilds)
                {
                    try
                    {
                        using (var context = new BotDb())
                        {
                            var forcedNicknames = await context.ForcedNicknames.Where(x=>x.GuildId == guild.Id).ToDictionaryAsync(x => x.UserId).ConfigureAwait(false);
                            var membersToUpdate = guild.Members.Where(x => forcedNicknames.ContainsKey(x.Key))
                                .Select(x => (discordMember: x.Value, forcedNickname: forcedNicknames[x.Key]))
                                .Where(x => x.discordMember.DisplayName != x.forcedNickname.Nickname)
                                .ToList();

                            foreach (var (discordMember, forcedNickname) in membersToUpdate)
                            {
                                await discordMember.ModifyAsync(x => x.Nickname = forcedNickname.Nickname).ConfigureAwait(false);
                            }
                        }

                    }
                    catch (Exception e)
                    {
                        Config.Log.Error(e);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(Config.ForcedNicknamesRecheckTimeInSeconds), Config.Cts.Token).ConfigureAwait(false);
            }
        }
    }
}