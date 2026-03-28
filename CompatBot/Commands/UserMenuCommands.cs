using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

/*
    [Command("📛 Enforce nickname"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Enforce specific nickname for a particular user permanently")]
    public static async ValueTask Rename(UserCommandContext ctx, DiscordUser discordUser)
    {
        var interactivity = ctx.Extension.ServiceProvider.GetService<InteractivityExtension>();
        if (interactivity is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't get interactivity extension").ConfigureAwait(false);
            return;
        }

        var interaction = ctx.Interaction;
        var suggestedName = await UsernameZalgoMonitor.GenerateRandomNameAsync(discordUser.Id).ConfigureAwait(false);
        var modal = new DiscordModalBuilder()
            .WithCustomId($"modal:nickname:{Guid.NewGuid():n}")
            .WithTitle("Enforcing Rule 7")
            .AddTextInput(new(
                "nickname",
                suggestedName,
                suggestedName,
                min_length: 2,
                max_length: 32
            ),
            "New nickname");
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        string resultMsg;
        try
        {
            InteractivityResult<ModalSubmittedEventArgs> modalResult;
            IModalSubmission? value;
            do
            {
                modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
                if (modalResult.TimedOut)
                    return;
            } while (
                !modalResult.Result.Values.TryGetValue("nickname", out value)
                || value is not TextInputModalSubmission { Value: { Length: > 1 and < 33 } textValue }
                || (!textValue.All(c => char.IsLetterOrDigit(c)
                                               || char.IsWhiteSpace(c)
                                               || char.IsPunctuation(c)
                    )
                    || textValue.Any(c => c is ':' or '#' or '@' or '`')
                ) && !discordUser.IsBotSafeCheck()
            );

            interaction = modalResult.Result.Interaction;
            await interaction.DeferAsync(true).ConfigureAwait(false);
            List<DiscordGuild> guilds;
            if (ctx.Guild is null)
                guilds = ctx.Client.Guilds.Values.ToList();
            else
                guilds = [ctx.Guild];

            var expectedNickname = ((TextInputModalSubmission)value).Value;
            int changed = 0, noPermissions = 0, failed = 0;
            await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
            foreach (var guild in guilds)
            {
                if (!discordUser.IsBotSafeCheck())
                {
                    var enforceRules = wdb.ForcedNicknames.FirstOrDefault(mem => mem.UserId == discordUser.Id && mem.GuildId == guild.Id);
                    if (enforceRules is null)
                    {
                        enforceRules = new() {UserId = discordUser.Id, GuildId = guild.Id, Nickname = expectedNickname};
                        await wdb.ForcedNicknames.AddAsync(enforceRules).ConfigureAwait(false);
                    }
                    else
                    {
                        if (enforceRules.Nickname == expectedNickname)
                            continue;

                        enforceRules.Nickname = expectedNickname;
                    }
                }
                if (!(ctx.Guild?.Permissions?.HasFlag(DiscordPermission.ChangeNickname) ?? true))
                {
                    noPermissions++;
                    continue;
                }

                if (await ctx.Client.GetMemberAsync(guild, discordUser).ConfigureAwait(false) is DiscordMember discordMember)
                    try
                    {
                        await discordMember.ModifyAsync(x => x.Nickname = expectedNickname).ConfigureAwait(false);
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        Config.Log.Warn(ex, "Failed to change nickname");
                        failed++;
                    }
            }
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            if (guilds.Count > 1)
            {
                if (changed > 0)
                    resultMsg = $"{Config.Reactions.Success} Forced nickname for {discordUser.Mention} in {changed} server{(changed == 1 ? "" : "s")}";
                else
                    resultMsg = $"{Config.Reactions.Failure} Failed to force nickname for {discordUser.Mention} in any server";
            }
            else
            {
                if (changed > 0)
                    resultMsg = $"{Config.Reactions.Success} Forced nickname for {discordUser.Mention}";
                else if (failed > 0)
                    resultMsg = $"{Config.Reactions.Failure} Failed to force nickname for {discordUser.Mention}";
                else if (noPermissions > 0)
                    resultMsg = $"{Config.Reactions.Failure} No permissions to force nickname for {discordUser.Mention}";
                else
                    resultMsg = "Unknown result, this situation should never happen";
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            resultMsg = $"{Config.Reactions.Failure} Failed to change nickname, check bot's permissions";
        }
        var msg = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithContent(resultMsg);
        await interaction.EditOriginalResponseAsync(new(msg)).ConfigureAwait(false);
    }
*/

/*
    [Command("🧼 Remove enforcement"), RequiresBotModRole, SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Remove nickname enforcement from a particular user")]
    public static async ValueTask Remove(UserCommandContext ctx, DiscordUser discordUser)
    {
        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        try
        {
            if (discordUser.IsBotSafeCheck() && ctx.Guild is not null)
            {
                if (await ctx.Client.GetMemberAsync(ctx.Guild.Id, discordUser).ConfigureAwait(false) is DiscordMember mem)
                {
                    await mem.ModifyAsync(m => m.Nickname = new(discordUser.Username)).ConfigureAwait(false);
                    await ctx.RespondAsync($"{Config.Reactions.Success} Reset server nickname to username", ephemeral: true).ConfigureAwait(false);
                }
                return;
            }

            await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
            var enforcedRules = ctx.Guild is null
                ? await wdb.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id).ToListAsync().ConfigureAwait(false)
                : await wdb.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id && mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
            if (enforcedRules is not {Count: >0})
                return;

            wdb.ForcedNicknames.RemoveRange(enforcedRules);
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            if (ctx.Guild is null)
                await ctx.RespondAsync($"{Config.Reactions.Success} Removed all nickname enforcements", ephemeral: true).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"{Config.Reactions.Success} Removed server nickname enforcement", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to reset user nickname", ephemeral: true).ConfigureAwait(false);
        }
    }
*/
}