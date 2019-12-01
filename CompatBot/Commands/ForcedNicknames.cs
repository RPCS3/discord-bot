using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("rename"), RequiresBotModRole]
    [Description("Manage users who has forced nickname.")]
    internal sealed class ForcedNicknames : BaseCommandModuleCustom
    {
        [GroupCommand]
        [Description("Enforces specific nickname for particular user.")]
        public async Task Add(CommandContext ctx, 
            [Description("Discord user to add to forced nickname list.")] DiscordUser discordUser, 
            [Description("Nickname which should be displayed.")] string expectedNickname)
        {
            try
            {
                if (expectedNickname.Length < 2 || expectedNickname.Length > 32)
                {
                    await ctx.ReactWithAsync( Config.Reactions.Failure, "Nickname must be between 2 and 32 characters long").ConfigureAwait(false);
                    return;
                }

                List<DiscordGuild> guilds;
                if (ctx.Guild == null)
                {
                    guilds = ctx.Client.Guilds?.Values.ToList() ?? new List<DiscordGuild>(0);
                    if (guilds.Count > 1)
                        await ctx.RespondAsync($"{discordUser.Mention} will be renamed in all {guilds.Count} servers").ConfigureAwait(false);
                }
                else
                    guilds = new List<DiscordGuild> {ctx.Guild};

                int changed = 0, noPermissions = 0, skipped = 0, failed = 0;
                using var db = new BotDb();
                foreach (var guild in guilds)
                {
                    var enforceRules = db.ForcedNicknames.FirstOrDefault(mem => mem.UserId == discordUser.Id && mem.GuildId == guild.Id);
                    if (enforceRules is null)
                    {
                        enforceRules = new ForcedNickname {UserId = discordUser.Id, GuildId = guild.Id, Nickname = expectedNickname};
                        db.ForcedNicknames.Add(enforceRules);
                    }
                    else
                    {
                        if (enforceRules.Nickname == expectedNickname)
                        {
                            skipped++;
                            continue;
                        }
                        enforceRules.Nickname = expectedNickname;
                    }
                    if (!(ctx.Guild?.Permissions?.HasFlag(Permissions.ChangeNickname) ?? true))
                    {
                        noPermissions++;
                        continue;
                    }

                    if (ctx.Client.GetMember(guild, discordUser) is DiscordMember discordMember)
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

        [Command("clear"), Aliases("remove")]
        [Description("Removes nickname restriction from particular user.")]
        public async Task Remove(CommandContext ctx, [Description("Discord user to remove from forced nickname list.")] DiscordUser discordUser)
        {
            try
            {
                using var db = new BotDb();
                var enforcedRules = ctx.Guild == null
                    ? await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id).ToListAsync().ConfigureAwait(false)
                    : await db.ForcedNicknames.Where(mem => mem.UserId == discordUser.Id && mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
                if (enforcedRules.Count == 0)
                    return;

                db.ForcedNicknames.RemoveRange(enforcedRules);
                await db.SaveChangesAsync().ConfigureAwait(false);
                foreach (var rule in enforcedRules)
                    if (ctx.Client.GetMember(rule.GuildId, discordUser) is DiscordMember discordMember)
                        try
                        {
                            //todo: change to mem.Nickname = default when the library fixes their shit
                            await discordMember.ModifyAsync(mem => mem.Nickname = new Optional<string>("")).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Config.Log.Debug(ex);
                        }
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to reset user nickname").ConfigureAwait(false);
            }
        }

        [Command("list")]
        [Description("Lists all users who has restricted nickname.")]
        public async Task List(CommandContext ctx)
        {
            using var db = new BotDb();
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
                await ctx.RespondAsync("No users with forced nicknames").ConfigureAwait(false);
                return;
            }

            var table = new AsciiTable(
                new AsciiColumn("ID", !ctx.Channel.IsPrivate || !ctx.User.IsWhitelisted(ctx.Client)),
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
}