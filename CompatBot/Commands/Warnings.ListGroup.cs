using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed partial class Warnings
    {
        [Group("list"), Aliases("show"), TriggersTyping]
        [Description("Allows to list warnings in various ways. Users can only see their own warnings.")]
        public class ListGroup : BaseCommandModuleCustom
        {
            [GroupCommand, Priority(10)]
            [Description("Show warning list for a user. Default is to show warning list for yourself")]
            public async Task List(CommandContext ctx, [Description("Discord user to list warnings for")] DiscordUser user)
            {
                if (await CheckListPermissionAsync(ctx, user.Id).ConfigureAwait(false))
                    await ListUserWarningsAsync(ctx.Client, ctx.Message, user.Id, user.Username.Sanitize(), false);
            }

            [GroupCommand]
            public async Task List(CommandContext ctx, [Description("Id of the user to list warnings for")] ulong userId)
            {
                if (await CheckListPermissionAsync(ctx, userId).ConfigureAwait(false))
                    await ListUserWarningsAsync(ctx.Client, ctx.Message, userId, $"<@{userId}>", false);
            }

            [GroupCommand]
            [Description("List your own warning list")]
            public async Task List(CommandContext ctx)
            {
                await List(ctx, ctx.Message.Author).ConfigureAwait(false);
            }

            [Command("users"), Aliases("top"), RequiresBotModRole, TriggersTyping]
            [Description("List users with warnings, sorted from most warned to least")]
            public async Task Users(CommandContext ctx, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                if (number < 1)
                    number = 10;
                var userIdColumn = ctx.Channel.IsPrivate ? $"{"User ID",-18} | " : "";
                var header = $"{"User",-25} | {userIdColumn}Count";
                var result = new StringBuilder("Warning count per user:").AppendLine("```")
                    .AppendLine(header)
                    .AppendLine("".PadLeft(header.Length, '-'));
                using (var db = new BotDb())
                {
                    var query = from warn in db.Warning
                        group warn by warn.DiscordId
                        into userGroup
                        let row = new {discordId = userGroup.Key, count = userGroup.Count()}
                        orderby row.count descending
                        select row;
                    foreach (var row in query.Take(number))
                    {
                        var username = await ctx.GetUserNameAsync(row.discordId).ConfigureAwait(false);
                        result.Append($"{username,-25} | ");
                        if (ctx.Channel.IsPrivate)
                            result.Append($"{row.discordId,-18} | ");
                        result.AppendLine($"{row.count,2}");
                    }
                }
                await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
            }

            [Command("recent"), Aliases("last", "all"), RequiresBotModRole, TriggersTyping]
            [Description("Shows last issued warnings in chronological order")]
            public async Task Last(CommandContext ctx, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                if (number < 1)
                    number = 10;
                var userIdColumn = ctx.Channel.IsPrivate ? $"{"User ID",-18} | " : "";
                var header = $"ID    | {"User",-25} | {userIdColumn}{"Issued by",-15} | {"On date (UTC)",-20} | Reason              ";
                var result = new StringBuilder("Last issued warnings:").AppendLine("```")
                    .AppendLine(header)
                    .AppendLine("".PadLeft(header.Length, '-'));
                using (var db = new BotDb())
                {
                    var query = from warn in db.Warning
                        orderby warn.Id descending
                        select warn;
                    foreach (var row in query.Take(number))
                    {
                        var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                        var modname = await ctx.GetUserNameAsync(row.IssuerId, defaultName: "Unknown mod").ConfigureAwait(false);
                        result.Append($"{row.Id:00000} | {username,-25} | ");
                        if (ctx.Channel.IsPrivate)
                            result.Append($"{row.DiscordId,-18} | ");
                        var timestamp = row.Timestamp.HasValue ? new DateTime(row.Timestamp.Value, DateTimeKind.Utc).ToString("u") : null;
                        result.Append($"{modname,-15} | {timestamp,-20} | {row.Reason}");
                        if (ctx.Channel.IsPrivate)
                            result.Append(" | ").Append(row.FullReason);
                        result.AppendLine();
                    }
                }
                await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
            }

            private async Task<bool> CheckListPermissionAsync(CommandContext ctx, ulong userId)
            {
                if (userId == ctx.Message.Author.Id || ModProvider.IsMod(ctx.Message.Author.Id))
                    return true;

                await ctx.ReactWithAsync(Config.Reactions.Denied, "Regular users can only view their own warnings").ConfigureAwait(false);
                return false;
            }
        }
    }
}
