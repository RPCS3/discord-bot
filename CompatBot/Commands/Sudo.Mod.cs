using System.Linq;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal partial class Sudo
    {
        [Group("mod")]
        [Description("Used to manage bot moderators")]
        public sealed class Mod : BaseCommandModuleCustom
        {
            [Command("add")]
            [Description("Adds a new moderator")]
            public async Task Add(CommandContext ctx, [Description("Discord user to add to the bot mod list")] DiscordMember user)
            {
                if (await ModProvider.AddAsync(user.Id).ConfigureAwait(false))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Success,
                        $"{user.Mention} was successfully added as moderator!\n" +
                         $"Try using `{ctx.Prefix}help` to see new commands available to you"
                    ).ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{user.Mention} is already a moderator").ConfigureAwait(false);
            }

            [Command("remove"), Aliases("delete", "del")]
            [Description("Removes a moderator")]
            public async Task Remove(CommandContext ctx, [Description("Discord user to remove from the bot mod list")] DiscordMember user)
            {
                if (ctx.Client.CurrentApplication.Owners.Any(u => u.Id == user.Id))
                {
                    var dm = await user.CreateDmChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync($@"Just letting you know that {ctx.Message.Author.Mention} just tried to strip you off of your mod role ¯\\_(ツ)_/¯").ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Denied, $"{ctx.Message.Author.Mention} why would you even try this?! Alerting {user.Mention}", true).ConfigureAwait(false);
                }
                else if (await ModProvider.RemoveAsync(user.Id).ConfigureAwait(false))
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"{user.Mention} removed as moderator!").ConfigureAwait(false);
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{user.Mention} is not a moderator").ConfigureAwait(false);
            }

            [Command("list"), Aliases("show")]
            [Description("Lists all moderators")]
            public async Task List(CommandContext ctx)
            {
                var table = new AsciiTable(
                    new AsciiColumn( "Username", maxWidth: 32),
                    new AsciiColumn("Sudo")
                );
                foreach (var mod in ModProvider.Mods.Values.OrderByDescending(m => m.Sudoer))
                    table.Add(await ctx.GetUserNameAsync(mod.DiscordId), mod.Sudoer ? "✅" :"");
                await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
            }

            [Command("sudo")]
            [Description("Makes a moderator a sudoer")]
            public async Task Sudo(CommandContext ctx, [Description("Discord user on the moderator list to grant the sudoer rights to")] DiscordMember moderator)
            {
                if (ModProvider.IsMod(moderator.Id))
                {
                    if (await ModProvider.MakeSudoerAsync(moderator.Id).ConfigureAwait(false))
                        await ctx.ReactWithAsync(Config.Reactions.Success, $"{moderator.Mention} is now a sudoer").ConfigureAwait(false);
                    else
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"{moderator.Mention} is already a sudoer").ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{moderator.Mention} is not a moderator (yet)").ConfigureAwait(false);
            }

            [Command("unsudo")]
            [Description("Makes a sudoer a regular moderator")]
            public async Task Unsudo(CommandContext ctx, [Description("Discord user on the moderator list to strip the sudoer rights from")] DiscordMember sudoer)
            {
                if (ctx.Client.CurrentApplication.Owners.Any(u => u.Id == sudoer.Id))
                {
                    var dm = await sudoer.CreateDmChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync($@"Just letting you know that {ctx.Message.Author.Mention} just tried to strip you off of your sudo permissions ¯\\_(ツ)_/¯").ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Denied, $"{ctx.Message.Author.Mention} why would you even try this?! Alerting {sudoer.Mention}", true).ConfigureAwait(false);
                }
                else if (ModProvider.IsMod(sudoer.Id))
                {
                    if (await ModProvider.UnmakeSudoerAsync(sudoer.Id).ConfigureAwait(false))
                        await ctx.ReactWithAsync(Config.Reactions.Success, $"{sudoer.Mention} is no longer a sudoer").ConfigureAwait(false);
                    else
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"{sudoer.Mention} is not a sudoer").ConfigureAwait(false);
                }
                else
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"{sudoer.Mention} is not even a moderator!").ConfigureAwait(false);
            }
        }
    }
}
