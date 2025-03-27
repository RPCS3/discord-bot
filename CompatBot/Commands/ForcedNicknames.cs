using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.UserCommands;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot.Commands;

[Description("Manage users who has forced nickname.")]
internal static class ForcedNicknames
{
    // limited to 5 commands per menu

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
        var modal = new DiscordInteractionResponseBuilder()
            .AsEphemeral()
            .WithCustomId($"modal:nickname:{Guid.NewGuid():n}")
            .WithTitle("Enforcing Rule 7")
            .AddComponents(
                new DiscordTextInputComponent(
                    "New nickname",
                    "nickname",
                    suggestedName,
                    suggestedName,
                    min_length: 2,
                    max_length: 32
                )
            );
        await ctx.RespondWithModalAsync(modal).ConfigureAwait(false);

        string resultMsg;
        try
        {
            InteractivityResult<ModalSubmittedEventArgs> modalResult;
            string expectedNickname;
            do
            {
                modalResult = await interactivity.WaitForModalAsync(modal.CustomId, ctx.User).ConfigureAwait(false);
                if (modalResult.TimedOut)
                    return;
            } while (
                !modalResult.Result.Values.TryGetValue("nickname", out expectedNickname)
                || expectedNickname is not { Length: >1 and <33 }
                || (!expectedNickname.All(c => char.IsLetterOrDigit(c)
                                                    || char.IsWhiteSpace(c)
                                                    || char.IsPunctuation(c)
                         )
                         || expectedNickname.Any(c => c is ':' or '#' or '@' or '`')
                   ) && !discordUser.IsBotSafeCheck()
            );

            interaction = modalResult.Result.Interaction;
            await interaction.DeferAsync(true).ConfigureAwait(false);
            List<DiscordGuild> guilds;
            if (ctx.Guild is null)
                guilds = ctx.Client.Guilds.Values.ToList();
            else
                guilds = [ctx.Guild];

            int changed = 0, noPermissions = 0, failed = 0;
            await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
            foreach (var guild in guilds)
            {
                if (!discordUser.IsBotSafeCheck())
                {
                    var enforceRules = db.ForcedNicknames.FirstOrDefault(mem => mem.UserId == discordUser.Id && mem.GuildId == guild.Id);
                    if (enforceRules is null)
                    {
                        enforceRules = new() {UserId = discordUser.Id, GuildId = guild.Id, Nickname = expectedNickname};
                        await db.ForcedNicknames.AddAsync(enforceRules).ConfigureAwait(false);
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
            await db.SaveChangesAsync().ConfigureAwait(false);
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

            await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
            var enforcedRules = ctx.Guild is null
                ? await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id).ToListAsync().ConfigureAwait(false)
                : await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id && mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
            if (enforcedRules is not {Count: >0})
                return;

            db.ForcedNicknames.RemoveRange(enforcedRules);
            await db.SaveChangesAsync().ConfigureAwait(false);
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

    /*
    [Command("cleanup"), TextAlias("clean", "fix"), RequiresBotModRole]
    [Description("Removes zalgo from specified user nickname")]
    public async Task Cleanup(CommandContext ctx, [Description("Discord user to clean up")] DiscordUser discordUser)
    {
        if (await ctx.Client.GetMemberAsync(discordUser).ConfigureAwait(false) is not DiscordMember member)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to resolve guild member for user {discordUser.Username}#{discordUser.Discriminator}").ConfigureAwait(false);
            return;
        }

        var name = member.DisplayName;
        var newName = UsernameZalgoMonitor.StripZalgo(name, discordUser.Username, discordUser.Id);
        if (name == newName)
            await ctx.Channel.SendMessageAsync("Failed to remove any extra symbols").ConfigureAwait(false);
        else
        {
            try
            {
                await member.ModifyAsync(m => m.Nickname = new(newName)).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed user to {newName}", true).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Config.Log.Warn($"Failed to rename user {discordUser.Username}#{discordUser.Discriminator}");
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to rename user to {newName}").ConfigureAwait(false);
            }
        }
    }
    */
    
    /*
    [Command("🔍 Dump"), SlashCommandTypes(DiscordApplicationCommandType.UserContextMenu)]
    [Description("Print hexadecimal binary representation of an UTF-8 encoded user name for diagnostic purposes")]
    public static async ValueTask Dump(UserCommandContext ctx, DiscordUser discordUser)
    {
        var name = discordUser.Username;
        var nameBytes = StringUtils.Utf8.GetBytes(name);
        var hex = BitConverter.ToString(nameBytes).Replace('-', ' ');
        var result = $"User ID: {discordUser.Id}\nUsername: {hex}";
        var member = await ctx.Client.GetMemberAsync(ctx.Guild, discordUser).ConfigureAwait(false);
        if (member is { Nickname: { Length: > 0 } nickname })
        {
            nameBytes = StringUtils.Utf8.GetBytes(nickname);
            hex = BitConverter.ToString(nameBytes).Replace('-', ' ');
            result += "\nNickname: " + hex;
        }
        await ctx.RespondAsync(result, ephemeral: true).ConfigureAwait(false);
    }
    */

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

    /*
    [Command("list"), RequiresBotModRole]
    [Description("Lists all users who has restricted nickname.")]
    public async Task List(CommandContext ctx)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        var selectExpr = db.ForcedNicknames.AsNoTracking();
        if (ctx.Guild != null)
            selectExpr = selectExpr.Where(mem => mem.GuildId == ctx.Guild.Id);

        var forcedNicknames = (
            from m in selectExpr.AsEnumerable()
            orderby m.UserId, m.Nickname
            let result = new {m.UserId, m.Nickname}
            select result
        ).ToList();
        if (forcedNicknames.Count == 0)
        {
            await ctx.Channel.SendMessageAsync("No users with forced nicknames").ConfigureAwait(false);
            return;
        }

        var table = new AsciiTable(
            new AsciiColumn("ID", !ctx.Channel.IsPrivate || !await ctx.User.IsWhitelistedAsync(ctx.Client).ConfigureAwait(false)),
            new AsciiColumn("Username"),
            new AsciiColumn("Forced nickname")
        );
        var previousUser = 0ul;
        foreach (var forcedNickname in forcedNicknames.Distinct())
        {
            var sameUser = forcedNickname.UserId == previousUser;
            var username = sameUser ? "" : await ctx.GetUserNameAsync(forcedNickname.UserId).ConfigureAwait(false);
            table.Add( sameUser ? "" : forcedNickname.UserId.ToString(), username, forcedNickname.Nickname);
            previousUser = forcedNickname.UserId;
        }
        await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
    }
    */
}
