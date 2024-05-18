﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Group("rename")]
[Description("Manage users who has forced nickname.")]
internal sealed class ForcedNicknames : BaseCommandModuleCustom
{
    [GroupCommand]
    [Description("Enforces specific nickname for particular user.")]
    public async Task Rename(CommandContext ctx,
        [Description("Discord user to add to forced nickname list.")] DiscordUser discordUser, 
        [Description("Nickname which should be displayed."), RemainingText] string expectedNickname)
    {
        if (!await new RequiresBotModRole().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
            return;
            
        try
        {
            if (expectedNickname.Length is < 2 or > 32)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Nickname must be between 2 and 32 characters long", true).ConfigureAwait(false);
                return;
            }

            if ((!expectedNickname.All(c => char.IsLetterOrDigit(c)
                                            || char.IsWhiteSpace(c)
                                            || char.IsPunctuation(c))
                 || expectedNickname.Any(c => c is ':' or '#' or '@' or '`')
                ) && !discordUser.IsBotSafeCheck())
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Nickname must follow Rule 7", true).ConfigureAwait(false);
                return;
            }

            List<DiscordGuild> guilds;
            if (ctx.Guild == null)
            {
                guilds = ctx.Client.Guilds?.Values.ToList() ?? [];
                if (guilds.Count > 1)
                    await ctx.Channel.SendMessageAsync($"{discordUser.Mention} will be renamed in all {guilds.Count} servers").ConfigureAwait(false);
            }
            else
                guilds = [ctx.Guild];

            int changed = 0, noPermissions = 0, failed = 0;
            await using var db = new BotDb();
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
                if (!(ctx.Guild?.Permissions?.HasFlag(Permissions.ChangeNickname) ?? true))
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
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Forced nickname for {discordUser.Mention} in {changed} server{(changed == 1 ? "" : "s")}", true).ConfigureAwait(false);
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to force nickname for {discordUser.Mention} in any server").ConfigureAwait(false);
            }
            else
            {
                if (changed > 0)
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Forced nickname for {discordUser.Mention}", true).ConfigureAwait(false);
                else if (failed > 0)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to force nickname for {discordUser.Mention}").ConfigureAwait(false);
                else if (noPermissions > 0)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"No permissions to force nickname for {discordUser.Mention}").ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to change nickname, check bot's permissions").ConfigureAwait(false);
        }
    }

    [Command("clear"), Aliases("remove"), RequiresBotModRole]
    [Description("Removes nickname restriction from particular user.")]
    public async Task Remove(CommandContext ctx, [Description("Discord user to remove from forced nickname list.")] DiscordUser discordUser)
    {
        try
        {
            if (discordUser.IsBotSafeCheck())
            {
                var mem = await ctx.Client.GetMemberAsync(ctx.Guild.Id, discordUser).ConfigureAwait(false);
                if (mem is not null)
                {
                    await mem.ModifyAsync(m => m.Nickname = new(discordUser.Username)).ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                }
                return;
            }
                
            await using var db = new BotDb();
            var enforcedRules = ctx.Guild == null
                ? await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id).ToListAsync().ConfigureAwait(false)
                : await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id && mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
            if (enforcedRules.Count == 0)
                return;
                
            db.ForcedNicknames.RemoveRange(enforcedRules);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to reset user nickname").ConfigureAwait(false);
        }
    }

    [Command("cleanup"), Aliases("clean", "fix"), RequiresBotModRole]
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

    [Command("dump")]
    [Description("Prints hexadecimal binary representation of an UTF-8 encoded user name for diagnostic purposes")]
    public async Task Dump(CommandContext ctx, [Description("Discord user to dump")] DiscordUser discordUser)
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
        await ctx.Channel.SendMessageAsync(result).ConfigureAwait(false);
    }

    [Command("generate"), Aliases("gen", "suggest")]
    [Description("Generates random name for specified user")]
    public async Task Generate(CommandContext ctx, [Description("Discord user to dump")] DiscordUser discordUser)
    {
        var newName = UsernameZalgoMonitor.GenerateRandomName(discordUser.Id);
        await ctx.Channel.SendMessageAsync(newName).ConfigureAwait(false);
    }

    [Command("autorename"), Aliases("auto"), RequiresBotModRole]
    [Description("Sets automatically generated nickname without enforcing it")]
    public async Task Autorename(CommandContext ctx, [Description("Discord user to rename")] DiscordUser discordUser)
    {
        var newName = UsernameZalgoMonitor.GenerateRandomName(discordUser.Id);
        try
        {
            if (await ctx.Client.GetMemberAsync(discordUser).ConfigureAwait(false) is { } member)
            {
                await member.ModifyAsync(m => m.Nickname = new(newName)).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed user to {newName}", true).ConfigureAwait(false);
            }
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Couldn't resolve guild member for user {discordUser.Username}#{discordUser.Discriminator}").ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to rename user {discordUser.Username}#{discordUser.Discriminator}");
            await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to rename user to {newName}").ConfigureAwait(false);
        }
    }
        
    [Command("list"), RequiresBotModRole]
    [Description("Lists all users who has restricted nickname.")]
    public async Task List(CommandContext ctx)
    {
        await using var db = new BotDb();
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
}