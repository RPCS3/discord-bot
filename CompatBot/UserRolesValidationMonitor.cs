using CompatBot.Database;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot;

internal static class UserRolesValidationMonitor
{
    public static async Task OnMemberAdded(DiscordClient client, GuildMemberAddedEventArgs args)
    {
        bool assignRole = false;
        using (var rdb = BotDb.OpenRead())
        {
            assignRole = await rdb.ForcedWarningRoles.AsNoTracking()
                .AnyAsync(wr => wr.UserId == args.Member.Id)
                .ConfigureAwait(false);
        }
        if (assignRole)
            await args.Member.AddRoleAsync(Config.WarnRoleId, client, args.Guild, "User previously had this role assigned").ConfigureAwait(false);
    }
}
