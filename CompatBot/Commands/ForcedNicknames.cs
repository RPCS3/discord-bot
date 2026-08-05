using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Description("Manage users who has forced nickname.")]
[Command("nickname"), RequiresBotModRole]
internal static class ForcedNicknames
{
    [Command("lock")]
    [Description("Enforce specific nickname for a particular user permanently")]
    public static async ValueTask Rename(SlashCommandContext ctx, DiscordUser user, string newNickname)
    {
        if (newNickname is { Length: < 3 or > 32 }
            || !newNickname.All(c => char.IsLetterOrDigit(c)
                                  || char.IsWhiteSpace(c)
                                  || char.IsPunctuation(c))
            || newNickname.Any(c => c is ':' or '#' or '@' or '`'))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Nicknames must be 2 to 32 characters long without special symbols", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var ephemeral = !ctx.Channel.IsSpamChannel();
        string resultMsg;
        try
        {
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            List<DiscordGuild> guilds;
            if (ctx.Guild is null)
                guilds = ctx.Client.Guilds.Values.ToList();
            else
                guilds = [ctx.Guild];

            int changed = 0, noPermissions = 0, failed = 0, enforced = 0, skipped = 0;
            await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
            foreach (var guild in guilds)
            {
                if (!user.IsBotSafeCheck())
                {
                    var enforceRules = wdb.ForcedNicknames.FirstOrDefault(mem => mem.UserId == user.Id && mem.GuildId == guild.Id);
                    if (enforceRules is null)
                    {
                        enforceRules = new() { UserId = user.Id, GuildId = guild.Id, Nickname = newNickname };
                        await wdb.ForcedNicknames.AddAsync(enforceRules).ConfigureAwait(false);
                    }
                    else
                    {
                        if (enforceRules.Nickname == newNickname)
                        {
                            skipped++;
                            continue;
                        }

                        enforceRules.Nickname = newNickname;
                        enforced++;
                    }
                }
                if (await ctx.Client.GetMemberAsync(guild, user).ConfigureAwait(false) is DiscordMember discordMember)
                    try
                    {
                        await discordMember.ModifyAsync(x => x.Nickname = newNickname).ConfigureAwait(false);
                        changed++;
                    }
                    catch (Exception ex)
                    {
                        Config.Log.Warn(ex, "Failed to change nickname");
                        failed++;
                    }
            }
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            if (guilds.Count >1)
            {
                if (changed > 0 || enforced > 0)
                    resultMsg = $"{Config.Reactions.Success} Forced nickname for {user.Mention} in {changed} server{(changed == 1 ? "" : "s")}";
                if (skipped > 0)
                    resultMsg = $"{Config.Reactions.Success} Nickname for {user.Mention} is already enforced";
                else
                    resultMsg = $"{Config.Reactions.Failure} Failed to force nickname for {user.Mention} in any server";
            }
            else
            {
                if (changed > 0 || enforced > 0)
                    resultMsg = $"{Config.Reactions.Success} Forced nickname for {user.Mention}";
                if (skipped > 0)
                    resultMsg = $"{Config.Reactions.Success} Nickname for {user.Mention} is already enforced";
                else if (failed > 0)
                    resultMsg = $"{Config.Reactions.Failure} Failed to force nickname for {user.Mention}";
                else if (noPermissions > 0)
                    resultMsg = $"{Config.Reactions.Failure} No permissions to force nickname for {user.Mention}";
                else
                    resultMsg = "Unknown result, this situation should never happen";
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            resultMsg = $"{Config.Reactions.Failure} Failed to change nickname, check bot's permissions";
        }
        await ctx.RespondAsync(resultMsg, ephemeral).ConfigureAwait(false);
    }

    [Command("unlock")]
    [Description("Remove nickname enforcement from a particular user")]
    public static async ValueTask Remove(SlashCommandContext ctx, DiscordUser user)
    {
        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        var ephemeral = !ctx.Channel.IsSpamChannel();
        try
        {
            if (user.IsBotSafeCheck() && ctx.Guild is not null)
            {
                if (await ctx.Client.GetMemberAsync(ctx.Guild.Id, user).ConfigureAwait(false) is DiscordMember mem)
                {
                    await mem.ModifyAsync(m => m.Nickname = new(user.Username)).ConfigureAwait(false);
                    await ctx.RespondAsync($"{Config.Reactions.Success} Reset server nickname to username for {mem.Mention}", ephemeral).ConfigureAwait(false);
                }
                return;
            }

            await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
            var enforcedRules = ctx.Guild is null
                ? await wdb.ForcedNicknames.Where(mem => mem.UserId == user.Id).ToListAsync().ConfigureAwait(false)
                : await wdb.ForcedNicknames.Where(mem => mem.UserId == user.Id && mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
            if (enforcedRules is not {Count: >0})
                return;

            wdb.ForcedNicknames.RemoveRange(enforcedRules);
            await wdb.SaveChangesAsync().ConfigureAwait(false);
            if (ctx.Guild is null)
                await ctx.RespondAsync($"{Config.Reactions.Success} Removed all nickname enforcements", ephemeral).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"{Config.Reactions.Success} Removed server nickname enforcement", ephemeral).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to reset user nickname", ephemeral).ConfigureAwait(false);
        }
    }

    [Command("cleanup")]
    [Description("Removes zalgo from specified user nickname")]
    public static async ValueTask Cleanup(SlashCommandContext ctx, DiscordMember user)
    {
        if (await ctx.Client.GetMemberAsync(user).ConfigureAwait(false) is not DiscordMember member)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to resolve guild member for user {user.Username}", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var ephemeral = !ctx.Channel.IsSpamChannel();
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        var name = member.DisplayName;
        var newName = await UsernameZalgoMonitor.StripZalgoAsync(name, user.Username, user.Id).ConfigureAwait(false);
        if (name == newName)
            await ctx.RespondAsync("Current nickname passed automated requirements check", ephemeral).ConfigureAwait(false);
        else
        {
            try
            {
                await member.ModifyAsync(m => m.Nickname = new(newName)).ConfigureAwait(false);
                await ctx.RespondAsync($"{Config.Reactions.Success} Renamed user to {newName}", ephemeral).ConfigureAwait(false);
            }
            catch (Exception)
            {
                Config.Log.Warn($"Failed to rename user {user.Username}#{user.Discriminator}");
                await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to rename user to {newName}", ephemeral).ConfigureAwait(false);
            }
        }
    }
    
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

    /*
    [Command("list"), RequiresBotModRole]
    [Description("Lists all users who have restricted nickname.")]
    public static async ValueTask List(SlashCommandContext ctx)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        var selectExpr = db.ForcedNicknames.AsNoTracking();
        if (ctx.Guild is not null)
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
