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
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed partial class Warnings
    {
        [Group("list"), Aliases("show")]
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
                var isMod = ctx.User.IsWhitelisted(ctx.Client, ctx.Guild);
                if (number < 1)
                    number = 10;
                var table = new AsciiTable(
                    new AsciiColumn("Username", maxWidth: 24),
                    new AsciiColumn("User ID", disabled: !ctx.Channel.IsPrivate, alignToRight: true),
                    new AsciiColumn("Count", alignToRight: true),
                    new AsciiColumn("Including removed", disabled: !ctx.Channel.IsPrivate || !isMod, alignToRight: true)
                    );
                using (var db = new BotDb())
                {
                    var query = from warn in db.Warning
                        group warn by warn.DiscordId
                        into userGroup
                        let row = new {discordId = userGroup.Key, count = userGroup.Count(w => !w.Retracted), total = userGroup.Count()}
                        orderby row.count descending
                        select row;
                    foreach (var row in query.Take(number))
                    {
                        var username = await ctx.GetUserNameAsync(row.discordId).ConfigureAwait(false);
                        table.Add(username, row.discordId.ToString(), row.count.ToString(), row.total.ToString());
                    }
                }
                await ctx.SendAutosplitMessageAsync(new StringBuilder("Warning count per user:").Append(table)).ConfigureAwait(false);
            }

            [Command("by"), RequiresBotModRole]
            [Description("Shows warnings issued by the specified moderator")]
            public async Task By(CommandContext ctx, ulong moderatorId, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                if (number < 1)
                    number = 10;
                var table = new AsciiTable(
                    new AsciiColumn("ID", alignToRight: true),
                    new AsciiColumn("Username", maxWidth: 24),
                    new AsciiColumn("User ID", disabled: !ctx.Channel.IsPrivate, alignToRight: true),
                    new AsciiColumn("On date (UTC)"),
                    new AsciiColumn("Reason"),
                    new AsciiColumn("Context", disabled: !ctx.Channel.IsPrivate)
                );
                using (var db = new BotDb())
                {
                    var query = from warn in db.Warning
                        where warn.IssuerId == moderatorId
                        orderby warn.Id descending
                        select warn;
                    foreach (var row in query.Take(number))
                    {
                        var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                        var timestamp = row.Timestamp.HasValue ? new DateTime(row.Timestamp.Value, DateTimeKind.Utc).ToString("u") : null;
                        table.Add(row.Id.ToString(), username, row.DiscordId.ToString(), timestamp, row.Reason, row.FullReason);
                    }
                }
                var modname = await ctx.GetUserNameAsync(moderatorId, defaultName: "Unknown mod").ConfigureAwait(false);
                await ctx.SendAutosplitMessageAsync(new StringBuilder($"Recent warnings issued by {modname}:").Append(table)).ConfigureAwait(false);

            }

            [Command("by"), Priority(1), RequiresBotModRole]
            public async Task By(CommandContext ctx, string me, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                if (me?.ToLowerInvariant() == "me")
                {
                    await By(ctx, ctx.User.Id, number).ConfigureAwait(false);
                    return;
                }

                var user = await new DiscordUserConverter().ConvertAsync(me, ctx).ConfigureAwait(false);
                if (user.HasValue)
                    await By(ctx, user.Value, number).ConfigureAwait(false);
            }

            [Command("by"), Priority(10), RequiresBotModRole]
            public Task By(CommandContext ctx, DiscordUser moderator, [Description("Optional number of items to show. Default is 10")] int number = 10)
                => By(ctx, moderator.Id, number);

            [Command("recent"), Aliases("last", "all"), RequiresBotModRole]
            [Description("Shows last issued warnings in chronological order")]
            public async Task Last(CommandContext ctx, [Description("Optional number of items to show. Default is 10")] int number = 10)
            {
                if (number < 1)
                    number = 10;
                var table = new AsciiTable(
                    new AsciiColumn("ID", alignToRight: true),
                    new AsciiColumn("Username", maxWidth: 24),
                    new AsciiColumn("User ID", disabled: !ctx.Channel.IsPrivate, alignToRight: true),
                    new AsciiColumn("Issued by", maxWidth: 15),
                    new AsciiColumn("On date (UTC)"),
                    new AsciiColumn("Reason"),
                    new AsciiColumn("Context", disabled: !ctx.Channel.IsPrivate)
                );
                using (var db = new BotDb())
                {
                    var query = from warn in db.Warning
                        orderby warn.Id descending
                        select warn;
                    foreach (var row in query.Take(number))
                    {
                        var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                        var modname = await ctx.GetUserNameAsync(row.IssuerId, defaultName: "Unknown mod").ConfigureAwait(false);
                        var timestamp = row.Timestamp.HasValue ? new DateTime(row.Timestamp.Value, DateTimeKind.Utc).ToString("u") : null;
                        table.Add(row.Id.ToString(), username, row.DiscordId.ToString(), modname, timestamp, row.Reason, row.FullReason);
                    }
                }
                await ctx.SendAutosplitMessageAsync(new StringBuilder("Recent warnings:").Append(table)).ConfigureAwait(false);
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
