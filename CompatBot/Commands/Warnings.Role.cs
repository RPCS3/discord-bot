using CompatBot.Database;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class Warnings
{
    [Command("role")]
    [Description("Manage Warning role")]
    internal static class Role
    {
        [Command("assign")]
        [Description("Assign Warning role to a user")]
        public static async ValueTask Assign(SlashCommandContext ctx, DiscordUser user, string? reason = null)
        {
            await ctx.DeferResponseAsync(ephemeral: true).ConfigureAwait(false);
            var alreadyAssigned = false;
            var errorMsg = "";
            using (var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false))
            {
                alreadyAssigned = await wdb.ForcedWarningRoles.AsNoTracking()
                    .AnyAsync(wr => wr.UserId == user.Id)
                    .ConfigureAwait(false);
                if (!alreadyAssigned)
                {
                    try
                    {
                        wdb.ForcedWarningRoles.Add(new() { UserId = user.Id });
                        await wdb.SaveChangesAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        errorMsg = $"❌ Failed to add role enforcement for user {user.DisplayName}";
                        Config.Log.Error(e, $"{errorMsg} ({user.Id})");
                    }
                }
            }
            await user.AddRoleAsync(Config.WarnRoleId, ctx.Client, ctx.Guild, reason ?? "no reason provided").ConfigureAwait(false);
            if (errorMsg is { Length: >0 })
                await ctx.RespondAsync(errorMsg, ephemeral: true).ConfigureAwait(false);
            else if (alreadyAssigned)
                await ctx.RespondAsync($"⚠️ Role has been already assigned for the user {user.DisplayName}", ephemeral: true).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"✅ Added role to the user {user.DisplayName}", ephemeral: true).ConfigureAwait(false);
        }

        [Command("remove")]
        [Description("Remove Warning role from a user")]
        public static async ValueTask Revoke(SlashCommandContext ctx, DiscordUser user, string? reason = null)
        {
            await ctx.DeferResponseAsync(ephemeral: true).ConfigureAwait(false);
            var alreadyRemoved = false;
            var errorMsg = "";
            using (var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false))
            {
                if (await wdb.ForcedWarningRoles
                    .FirstOrDefaultAsync(wr => wr.UserId == user.Id)
                    .ConfigureAwait(false) is ForcedWarningRole fwr)
                {
                    try
                    {
                        wdb.ForcedWarningRoles.Remove(fwr);
                        await wdb.SaveChangesAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        errorMsg = $"❌ Failed to remove role enforcement for user {user.DisplayName}";
                        Config.Log.Error(e, $"{errorMsg} ({user.Id})");
                    }
                }
                else
                    alreadyRemoved = true;
            }
            await user.RemoveRoleAsync(Config.WarnRoleId, ctx.Client, ctx.Guild, reason ?? "no reason provided").ConfigureAwait(false);
            if (errorMsg is { Length: > 0 })
                await ctx.RespondAsync(errorMsg, ephemeral: true).ConfigureAwait(false);
            else if (alreadyRemoved)
                await ctx.RespondAsync($"⚠️ User {user.DisplayName} does not have role enforcement", ephemeral: true).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"✅ Removed role from the user {user.DisplayName}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
