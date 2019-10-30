using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("forced-nicknames"), RequiresWhitelistedRole]
    [Description("Manage users who has forced nickname.")]
    internal sealed class ForcedNicknames : BaseCommandModuleCustom
    {
        [Command("add")]
        [Description("Enforces specific nickname for particular user.")]
        public async Task Add(CommandContext ctx, 
            [Description("Discord user to add to forced nickname list.")] DiscordMember discordMember, 
            [Description("Nickname which should be displayed.")] string expectedNickname)
        {
            using (var context = new BotDb())
            {
                if (!(await context.ForcedNicknames.SingleOrDefaultAsync(x => x.UserId == discordMember.Id).ConfigureAwait(false) is null))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{discordMember.Mention} is already on blacklist.").ConfigureAwait(false);
                    return;
                }

                context.ForcedNicknames.Add(
                    new ForcedNickname {UserId = discordMember.Id, Nickname = expectedNickname}
                );
                await context.SaveChangesAsync().ConfigureAwait(false);

                await discordMember.ModifyAsync(x => x.Nickname = expectedNickname).ConfigureAwait(false);

                await ctx.ReactWithAsync(Config.Reactions.Success,
                    $"{discordMember.Mention} was successfully added to blacklist!\n" +
                    $"Try using `{ctx.Prefix}help` to see new commands available to you"
                ).ConfigureAwait(false);
            }
        }

        [Command("remove")]
        [Description("Removes nickname restriction from particular user.")]
        public async Task Remove(CommandContext ctx, 
            [Description("Discord user to remove from forced nickname list.")] DiscordMember discordMember)
        {
            using (var context = new BotDb())
            {
                var forcedNickname = await context.ForcedNicknames.SingleOrDefaultAsync(x => x.UserId == discordMember.Id).ConfigureAwait(false);
                if (forcedNickname is null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{discordMember.Mention} is not on blacklist.").ConfigureAwait(false);
                    return;
                }

                context.ForcedNicknames.Remove(forcedNickname);
                await context.SaveChangesAsync().ConfigureAwait(false);

                await ctx.ReactWithAsync(Config.Reactions.Success,
                    $"{discordMember.Mention} was successfully removed from blacklist!\n" +
                    $"Try using `{ctx.Prefix}help` to see new commands available to you"
                ).ConfigureAwait(false);
            }
        }

        [Command("list")]
        [Description("Lists all users who has restricted nickname.")]
        public async Task List(CommandContext ctx)
        {
            using (var context = new BotDb())
            {
                var forcedNicknames = await context.ForcedNicknames.AsNoTracking().ToListAsync().ConfigureAwait(false);

                var displayString = forcedNicknames.Select(x =>
                        $"User: {ctx.Client.GetMember(x.UserId).Username}, forced nickname: {x.Nickname}")
                    .Aggregate(new StringBuilder(), (agg, x) => agg.AppendJoin("\n", x),x=>x.ToString());

                if (string.IsNullOrEmpty(displayString))
                    displayString = "Not found any forced nicknames.";

                await ctx.RespondAsync(displayString).ConfigureAwait(false);
            }
        } 
    }
}