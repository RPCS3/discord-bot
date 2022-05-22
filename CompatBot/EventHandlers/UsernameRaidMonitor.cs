using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    public static class UsernameRaidMonitor
    {
        public static async Task OnMemberUpdated(DiscordClient c, GuildMemberUpdateEventArgs args)
        {
            try
            {
                //member object most likely will not be updated in client cache at this moment
                string? fallback;
                if (args.NicknameAfter is string name)
                    fallback = args.Member.Username;
                else
                {
                    name = args.Member.Username;
                    fallback = null;
                }

                var member = await args.Guild.GetMemberAsync(args.Member.Id).ConfigureAwait(false) ?? args.Member;
                if (NeedsKick(name))
                {
                    await args.Member.RemoveAsync("Anti Raid").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task OnMemberAdded(DiscordClient c, GuildMemberAddEventArgs args)
        {
            try
            {
                var name = args.Member.DisplayName;
                if (NeedsKick(name))
                {
                    await args.Member.RemoveAsync("Anti Raid").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static bool NeedsKick(string displayName)
        {
            displayName = displayName.Normalize().TrimEager();
            return displayName.Equals("D𝗂scord");
        }

    }
}
