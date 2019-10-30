using System;
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
        public static async Task OnUserUpdated(UserUpdateEventArgs args)
        {
            await UpdateDisplayName(args.Client, () => args.Client.GetMember(args.UserAfter)).ConfigureAwait(false);
        }

        public static async Task OnMemberUpdated(GuildMemberUpdateEventArgs args)
        {
            await UpdateDisplayName(args.Client, () => args.Member).ConfigureAwait(false);
        }

        public static async Task OnMemberAdded(GuildMemberAddEventArgs args)
        {
            await UpdateDisplayName(args.Client, () => args.Member).ConfigureAwait(false);
        }

        private static async Task UpdateDisplayName(DiscordClient client, Func<DiscordMember> getGuildMember)
        {
            try
            {
                using (var context = new BotDb())
                {
                    var guildMember = getGuildMember();
                    var forcedNickname = await context.ForcedNicknames.FirstOrDefaultAsync(x => x.UserId == guildMember.Id).ConfigureAwait(false);
                    if (forcedNickname is null)
                        return;
                    
                    if (guildMember.DisplayName == forcedNickname.Nickname)
                        return;

                    await guildMember.ModifyAsync(x => x.Nickname = forcedNickname.Nickname).ConfigureAwait(false);

                    await client.ReportAsync(
                        "User nickname was changed.",
                        $"",
                        null,
                        ReportSeverity.Low
                    ).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }
    }
}