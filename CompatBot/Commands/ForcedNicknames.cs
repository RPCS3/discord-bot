using System;
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
            [Description("Discord user to add to forced nickname list.")] DiscordMember discordMember, 
            [Description("Nickname which should be displayed.")] string expectedNickname)
        {
            try
            {
                if (!(ctx.Guild?.Permissions?.HasFlag(Permissions.ChangeNickname) ?? true))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Bot doesn't have required permissions to rename other members", true).ConfigureAwait(false);
                    return;
                }

                if (expectedNickname.Length < 2 || expectedNickname.Length > 32)
                {
                    await ctx.ReactWithAsync( Config.Reactions.Failure, "Nickname must be between 2 and 32 characters long").ConfigureAwait(false);
                    return;
                }

                using var context = new BotDb();
                var forcedNickname = context.ForcedNicknames.SingleOrDefault(mem => mem.UserId == discordMember.Id && mem.GuildId == discordMember.Guild.Id);
                if (forcedNickname is null)
                {
                    forcedNickname = new ForcedNickname {UserId = discordMember.Id, GuildId = discordMember.Guild.Id, Nickname = expectedNickname};
                    context.ForcedNicknames.Add(forcedNickname);
                }
                else
                {
                    if (forcedNickname.Nickname == expectedNickname)
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure, "User already has this nickname forced").ConfigureAwait(false);
                        return;
                    }

                    forcedNickname.Nickname = expectedNickname;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
                await discordMember.ModifyAsync(x => x.Nickname = expectedNickname).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Forced nickname for {discordMember}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to change nickname, check bot's permissions").ConfigureAwait(false);
            }
        }

        [Command("clear"), Aliases("remove")]
        [Description("Removes nickname restriction from particular user.")]
        public async Task Remove(CommandContext ctx, [Description("Discord user to remove from forced nickname list.")] DiscordMember discordMember)
        {
            try
            {
                using var context = new BotDb();
                var forcedNickname = context.ForcedNicknames.SingleOrDefault(mem => mem.UserId == discordMember.Id && mem.GuildId == discordMember.Guild.Id);
                if (forcedNickname is null)
                    return;

                context.ForcedNicknames.Remove(forcedNickname);
                await context.SaveChangesAsync().ConfigureAwait(false);
                await discordMember.ModifyAsync(mem => mem.Nickname = default).ConfigureAwait(false);
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
            using var context = new BotDb();
            var forcedNicknames = await context.ForcedNicknames.AsNoTracking().Where(mem => mem.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);
            var table = new AsciiTable(
                new AsciiColumn("ID", !(ctx.Channel.IsPrivate || ctx.User.IsWhitelisted(ctx.Client))),
                new AsciiColumn("Username"),
                new AsciiColumn("Forced nickname")
            );
            foreach (var forcedNickname in forcedNicknames)
            {
                var username = await ctx.GetUserNameAsync(forcedNickname.UserId).ConfigureAwait(false);
                table.Add(forcedNickname.UserId.ToString(), username, forcedNickname.Nickname);
            }
            await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
        } 
    }
}