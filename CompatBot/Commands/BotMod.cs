using CompatBot.Database.Providers;
using DSharpPlus.Commands.Processors.UserCommands;

namespace CompatBot.Commands;

[Description("Used to manage bot moderators"), RequiresBotSudoerRole]
internal static class Mod
{
    [Command("💛 Give bot mod permissions"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static async ValueTask Add(UserCommandContext ctx, DiscordUser user)
    {
        if (await ModProvider.AddAsync(user.Id).ConfigureAwait(false))
        {
            var response = new DiscordInteractionResponseBuilder()
                .WithContent($"{Config.Reactions.Success} {user.Mention} was successfully added as moderator!")
                .AddMention(UserMention.All);
            await ctx.RespondAsync(response).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} {user.Mention} is already a moderator", ephemeral: true).ConfigureAwait(false);
    }

    [Command("💔 Remove bot mod permissions"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static async ValueTask Remove(UserCommandContext ctx, DiscordUser user)
    {
        if (ctx.Client.CurrentApplication.Owners?.Any(u => u.Id == user.Id) ?? false)
        {
            await ctx.RespondAsync($"{Config.Reactions.Denied} Why would you even try this?! Alerting {user.Mention}").ConfigureAwait(false);
            var dm = await user.CreateDmChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync($@"Just letting you know that {ctx.User.Mention} just tried to strip you off of your mod role ¯\\\_(ツ)\_/¯").ConfigureAwait(false);
        }
        else if (await ModProvider.RemoveAsync(user.Id).ConfigureAwait(false))
            await ctx.RespondAsync($"{Config.Reactions.Success} {user.Mention} removed as bot moderator", ephemeral: true).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} {user.Mention} is not a bot moderator", ephemeral: true).ConfigureAwait(false);
    }

    [Command("💜 Give bot admin permissions"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    public static async ValueTask Sudo(UserCommandContext ctx, DiscordUser moderator)
    {
        if (ModProvider.IsMod(moderator.Id))
        {
            if (await ModProvider.MakeSudoerAsync(moderator.Id).ConfigureAwait(false))
                await ctx.RespondAsync($"{Config.Reactions.Success} {moderator.Mention} is now a sudoer").ConfigureAwait(false);
            else
                await ctx.RespondAsync($"{Config.Reactions.Failure} {moderator.Mention} is already a sudoer", ephemeral: true).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} {moderator.Mention} is not a moderator (yet)", ephemeral: true).ConfigureAwait(false);
    }

    [Command("💔 Remove bot admin permissions")]
    public static async ValueTask Unsudo(UserCommandContext ctx, DiscordUser sudoer)
    {
        if (ctx.Client.CurrentApplication.Owners?.Any(u => u.Id == sudoer.Id) ?? false)
        {
            await ctx.RespondAsync($"{Config.Reactions.Denied} Why would you even try this?! Alerting {sudoer.Mention}").ConfigureAwait(false);
            var dm = await sudoer.CreateDmChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync($@"Just letting you know that {ctx.User.Mention} just tried to strip you off of your bot admin permissions ¯\\_(ツ)_/¯").ConfigureAwait(false);
        }
        else if (ModProvider.IsMod(sudoer.Id))
        {
            if (await ModProvider.UnmakeSudoerAsync(sudoer.Id).ConfigureAwait(false))
                await ctx.RespondAsync($"{Config.Reactions.Success} {sudoer.Mention} is no longer a bot admin", ephemeral: true).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"{Config.Reactions.Failure} {sudoer.Mention} is not a bot admin", ephemeral: true).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} {sudoer.Mention} is not even a bot mod!", ephemeral: true).ConfigureAwait(false);
    }
}
