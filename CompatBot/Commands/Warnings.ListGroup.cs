using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using CompatBot.Attributes;
using CompatBot.Database;
using CompatBot.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed partial class Warnings
    {
        [Group("list"), Aliases("show")]
        [Description("Allows to list warnings in various ways. Users can only see their own warnings.")]
        public class ListGroup : BaseCommandModule
        {
            [GroupCommand, Priority(10)]
            [Description("Show warning list for a user. Default is to show warning list for yourself")]
            public async Task List(CommandContext ctx, [Description("Discord user to warn")] DiscordUser user)
            {
                var typingTask = ctx.TriggerTypingAsync();
                if (await CheckListPermissionAsync(ctx, user.Id).ConfigureAwait(false))
                    await ListUserWarningsAsync(ctx.Client, ctx.Message, user.Id, user.Username.Sanitize(), false);
                await typingTask.ConfigureAwait(false);
            }

            [GroupCommand]
            public async Task List(CommandContext ctx, [Description("Id of the user to warn")] ulong userId)
            {
                var typingTask = ctx.TriggerTypingAsync();
                if (await CheckListPermissionAsync(ctx, userId).ConfigureAwait(false))
                    await ListUserWarningsAsync(ctx.Client, ctx.Message, userId, $"<@{userId}>", false);
                await typingTask.ConfigureAwait(false);
            }

            [GroupCommand]
            [Description("Show your own warning list")]
            public async Task List(CommandContext ctx)
            {
                var typingTask = ctx.TriggerTypingAsync();
                await List(ctx, ctx.Message.Author).ConfigureAwait(false);
                await typingTask.ConfigureAwait(false);
            }

            [Command("users"), RequiresBotModRole]
            [Description("List users with warnings, sorted from most warned to least")]
            public async Task Users(CommandContext ctx)
            {
               await ctx.TriggerTypingAsync().ConfigureAwait(false);
                var userIdColumn = ctx.Channel.IsPrivate ? $"{"User ID",-18} | " : "";
                var header = $"{"User",-25} | {userIdColumn}Count";
                var result = new StringBuilder("Warning count per user:").AppendLine("```")
                    .AppendLine(header)
                    .AppendLine("".PadLeft(header.Length, '-'));
                var query = from warn in BotDb.Instance.Warning
                    group warn by warn.DiscordId into userGroup
                    let row = new { discordId = userGroup.Key, count = userGroup.Count() }
                    orderby row.count descending
                    select row;
                foreach (var row in query)
                {
                    var username = await ctx.GetUserNameAsync(row.discordId).ConfigureAwait(false);
                    result.Append($"{username,-25} | ");
                    if (ctx.Channel.IsPrivate)
                        result.Append($"{row.discordId,-18} | ");
                    result.AppendLine($"{row.count,2}");
                }
                await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
            }

            [Command("recent"), Aliases("last", "all"), RequiresBotModRole]
            [Description("Shows last issued warnings in chronological order")]
            public async Task Last(CommandContext ctx, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
                if (number < 1)
                    number = 10;
                var userIdColumn = ctx.Channel.IsPrivate ? $"{"User ID",-18} | " : "";
                var header = $"ID    | {"User",-25} | {userIdColumn}{"Issued by",-25} | Reason              ";
                var result = new StringBuilder("Last issued warnings:").AppendLine("```")
                    .AppendLine(header)
                    .AppendLine("".PadLeft(header.Length, '-'));
                var query = from warn in BotDb.Instance.Warning
                    orderby warn.Id descending
                    select warn;
                foreach (var row in query.Take(number))
                {
                    var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                    var modname = await ctx.GetUserNameAsync(row.IssuerId, defaultName: "Unknown mod").ConfigureAwait(false);
                    result.Append($"{row.Id:00000} | {username,-25} | ");
                    if (ctx.Channel.IsPrivate)
                        result.Append($"{row.DiscordId,-18} | ");
                    result.Append($"{modname,-25} | {row.Reason}");
                    if (ctx.Channel.IsPrivate)
                        result.Append(" | ").Append(row.FullReason);
                    result.AppendLine();
                }
                await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
            }

            private async Task<bool> CheckListPermissionAsync(CommandContext ctx, ulong userId)
            {
                if (userId == ctx.Message.Author.Id || ModProvider.IsMod(ctx.Message.Author.Id))
                    return true;

                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Denied),
                    ctx.RespondAsync("Regular users can only view their own warnings")
                ).ConfigureAwait(false);
                return false;
            }
        }
    }
}
