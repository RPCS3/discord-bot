using System.Text;
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
        public sealed class Mod : BaseCommandModule
        {
            [Command("add")]
            [Description("Adds a new moderator")]
            public async Task Add(CommandContext ctx, [Description("Discord user to add to the bot mod list")] DiscordMember user)
            {
                var typingTask = ctx.TriggerTypingAsync();
                (DiscordEmoji reaction, string msg) result =
                    await ModProvider.AddAsync(user.Id).ConfigureAwait(false)
                        ? (Config.Reactions.Success, $"{user.Mention} was successfully added as moderator, you now have access to editing the piracy trigger list and other useful things! " +
                                                     "I will send you the available commands to your message box!")
                        : (Config.Reactions.Failure, $"{user.Mention} is already a moderator");
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(result.reaction),
                    ctx.RespondAsync(result.msg),
                    typingTask
                ).ConfigureAwait(false);
            }

            [Command("remove"), Aliases("delete", "del")]
            [Description("Removes a moderator")]
            public async Task Remove(CommandContext ctx, [Description("Discord user to remove from the bot mod list")] DiscordMember user)
            {
                var typingTask = ctx.TriggerTypingAsync();
                (DiscordEmoji reaction, string msg) result;
                if (user.Id == Config.BotAdminId)
                {
                    result = (Config.Reactions.Denied, $"{ctx.Message.Author.Mention} why would you even try this?! Alerting {user.Mention}");
                    var dm = await user.CreateDmChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync($@"Just letting you know that {ctx.Message.Author.Mention} just tried to strip you off of your mod role ¯\_(ツ)_/¯").ConfigureAwait(false);
                }
                else if (await ModProvider.RemoveAsync(user.Id).ConfigureAwait(false))
                    result = (Config.Reactions.Success, $"{user.Mention} removed as moderator!");
                else
                    result = (Config.Reactions.Failure, $"{user.Mention} is not a moderator");
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(result.reaction),
                    ctx.RespondAsync(result.msg),
                    typingTask
                ).ConfigureAwait(false);
            }

            [Command("list"), Aliases("show")]
            [Description("Lists all moderators")]
            public async Task List(CommandContext ctx)
            {
                var typingTask = ctx.TriggerTypingAsync();
                var list = new StringBuilder("```");
                foreach (var mod in ModProvider.Mods.Values)
                    list.AppendLine($"{await ctx.GetUserNameAsync(mod.DiscordId),-32} | {(mod.Sudoer ? "sudo" : "not sudo")}");
                await ctx.SendAutosplitMessageAsync(list.Append("```")).ConfigureAwait(false);
                await typingTask.ConfigureAwait(false);
            }

            [Command("sudo")]
            [Description("Makes a moderator a sudoer")]
            public async Task Sudo(CommandContext ctx, [Description("Discord user on the moderator list to grant the sudoer rights to")] DiscordMember moderator)
            {
                var typingTask = ctx.TriggerTypingAsync();
                (DiscordEmoji reaction, string msg) result;
                if (ModProvider.IsMod(moderator.Id))
                {
                    result = await ModProvider.MakeSudoerAsync(moderator.Id).ConfigureAwait(false)
                        ? (Config.Reactions.Success, $"{moderator.Mention} is now a sudoer")
                        : (Config.Reactions.Failure, $"{moderator.Mention} is already a sudoer");
                }
                else
                    result = (Config.Reactions.Failure, $"{moderator.Mention} is not a moderator (yet)");
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(result.reaction),
                    ctx.RespondAsync(result.msg),
                    typingTask
                ).ConfigureAwait(false);
            }

            [Command("unsudo")]
            [Description("Makes a sudoer a regular moderator")]
            public async Task Unsudo(CommandContext ctx, [Description("Discord user on the moderator list to strip the sudoer rights from")] DiscordMember sudoer)
            {
                var typingTask = ctx.TriggerTypingAsync();
                (DiscordEmoji reaction, string msg) result;
                if (sudoer.Id == Config.BotAdminId)
                {
                    result = (Config.Reactions.Denied, $"{ctx.Message.Author.Mention} why would you even try this?! Alerting {sudoer.Mention}");
                    var dm = await sudoer.CreateDmChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync($@"Just letting you know that {ctx.Message.Author.Mention} just tried to strip you off of your sudo permissions ¯\_(ツ)_/¯").ConfigureAwait(false);
                }
                else if (ModProvider.IsMod(sudoer.Id))
                {
                    result = await ModProvider.UnmakeSudoerAsync(sudoer.Id).ConfigureAwait(false)
                        ? (Config.Reactions.Success, $"{sudoer.Mention} is no longer a sudoer")
                        : (Config.Reactions.Failure, $"{sudoer.Mention} is not a sudoer");
                }
                else
                    result = (Config.Reactions.Failure, $"{sudoer.Mention} is not even a moderator!");
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(result.reaction),
                    ctx.RespondAsync(result.msg),
                    typingTask
                ).ConfigureAwait(false);
            }
        }
    }
}
