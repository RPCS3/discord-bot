using CompatBot.EventHandlers;
using DSharpPlus.Commands.Processors.UserCommands;

namespace CompatBot.Commands;

internal static class UserMenuCommands
{
    // limited to 5 commands per menu

    [Command("❗ Warn"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static ValueTask WarnUser(UserCommandContext ctx, DiscordUser user)
        => Warnings.Warn(ctx, null, user);

    [Command("🔍 Show warnings"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu), AllowDMUsage]
    public static ValueTask ShowWarnings(UserCommandContext ctx, DiscordUser user)
        => Warnings.ListGroup.List(ctx, user);
    
    [Command("📝 Rename automatically"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Set automatically generated nickname without enforcing it")]
    public static async ValueTask Autorename(UserCommandContext ctx, DiscordUser discordUser)
    {
        var newName = await UsernameZalgoMonitor.GenerateRandomNameAsync(discordUser.Id).ConfigureAwait(false);
        try
        {
            if (await ctx.Client.GetMemberAsync(discordUser).ConfigureAwait(false) is { } member)
            {
                await member.ModifyAsync(m => m.Nickname = new(newName)).ConfigureAwait(false);
                await ctx.RespondAsync($"{Config.Reactions.Success} Renamed user to {newName}", ephemeral: true).ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't resolve guild member for user {discordUser.Username}#{discordUser.Discriminator}", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to rename user {discordUser.Username}#{discordUser.Discriminator}");
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to rename user to {newName}", ephemeral: true).ConfigureAwait(false);
        }
    }

    [Command("📛 Assign Warning role"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static ValueTask AddWarnRole(UserCommandContext ctx, DiscordUser user)
        => Warnings.Role.Assign(ctx, user);

    [Command("🧼 Remove Warning role"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static ValueTask RemoveWarnRole(UserCommandContext ctx, DiscordUser user)
        => Warnings.Role.Revoke(ctx, user);
}