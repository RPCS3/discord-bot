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
                using (var context = new BotDb())
                {
                    var forcedNickname = context.ForcedNicknames.SingleOrDefault(x => x.UserId == discordMember.Id && x.GuildId == discordMember.Guild.Id);
                    if (forcedNickname is {})
                    {
                        await ChangeNickname(discordMember, expectedNickname, forcedNickname, context,ctx);

                        await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                        return;
                    }

                    context.ForcedNicknames.Add(
                        new ForcedNickname {UserId = discordMember.Id, GuildId = discordMember.Guild.Id, Nickname = expectedNickname}
                    );
                    await context.SaveChangesAsync().ConfigureAwait(false);

                    await discordMember.ModifyAsync(x => x.Nickname = expectedNickname).ConfigureAwait(false);

                    await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task ChangeNickname(DiscordMember discordMember, string expectedNickname,
            ForcedNickname forcedNickname, BotDb context, CommandContext ctx)
        {
            if (forcedNickname.Nickname == expectedNickname)
                return;

            forcedNickname.Nickname = expectedNickname;
            await context.SaveChangesAsync().ConfigureAwait(false);

            await discordMember.ModifyAsync(x => x.Nickname = expectedNickname).ConfigureAwait(false);
        }

        [Command("clear")]
        [Aliases("remove")]
        [Description("Removes nickname restriction from particular user.")]
        public async Task Remove(CommandContext ctx, 
            [Description("Discord user to remove from forced nickname list.")] DiscordMember discordMember)
        {
            try
            {
                using (var context = new BotDb())
                {
                    var forcedNickname = context.ForcedNicknames.SingleOrDefault(x => x.UserId == discordMember.Id && x.GuildId == discordMember.Guild.Id);
                    if (forcedNickname is null)
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"{discordMember.Mention} is not on blacklist.").ConfigureAwait(false);
                        return;
                    }

                    context.ForcedNicknames.Remove(forcedNickname);
                    await context.SaveChangesAsync().ConfigureAwait(false);

                    await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                throw;
            }
        }

        [Command("list")]
        [Description("Lists all users who has restricted nickname.")]
        public async Task List(CommandContext ctx)
        {
            using (var context = new BotDb())
            {
                var forcedNicknames = await context.ForcedNicknames.AsNoTracking().Where(x=>x.GuildId == ctx.Guild.Id).ToListAsync().ConfigureAwait(false);

                var table = new AsciiTable(
                    new AsciiColumn("ID", !ctx.Channel.IsPrivate),
                    new AsciiColumn("Username", maxWidth: 15),
                    new AsciiColumn("Forced nickname")
                );

                foreach (var forcedNickname in forcedNicknames)
                {
                    var username = await ctx.GetUserNameAsync(forcedNickname.UserId).ConfigureAwait(false);
                    table.Add(forcedNickname.UserId.ToString(), username, forcedNickname.Nickname);
                }

                await ctx.RespondAsync(table.ToString()).ConfigureAwait(false);
            }
        } 
    }
}