using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using DSharpPlus.Commands.Converters;

namespace CompatBot.Commands;

internal static partial class Warnings
{
    [Command("list")]
    [Description("Allows to list warnings in various ways. Users can only see their own warnings.")]
    internal static class ListGroup
    {
        [Command("user")]
        [Description("Show warning list for a user")]
        public static async ValueTask List(SlashCommandContext ctx, DiscordUser user)
        {
            await ctx.DeferResponseAsync(true).ConfigureAwait(false);
            if (await CheckListPermissionAsync(ctx, user.Id).ConfigureAwait(false))
                await ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize(), skipIfOne: false, useFollowup: true);
        }

        [Command("top")]
        [Description("List top users with warnings")]
        public static async ValueTask Users(
            SlashCommandContext ctx,
            [Description("Number of items to show. Default is 10")]
            int number = 10
        )
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            try
            {
                if (number < 1)
                    number = 10;
                var table = new AsciiTable(
                    new AsciiColumn("Username", maxWidth: 24),
                    new AsciiColumn("User ID", disabled: !ctx.Channel.IsPrivate, alignToRight: true),
                    new AsciiColumn("Count", alignToRight: true),
                    new AsciiColumn("All time", alignToRight: true)
                );
                await using var db = BotDb.OpenRead();
                var query = from warn in db.Warning.AsEnumerable()
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
                var pages = AutosplitResponseHelper.AutosplitMessage(new StringBuilder("Warning count per user:").Append(table).ToString());
                await ctx.RespondAsync(pages[0], ephemeral: ephemeral).ConfigureAwait(false);
                foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                    await ctx.FollowupAsync(page, ephemeral: ephemeral).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to execute the command: {e.Message}".Trim(EmbedPager.MaxMessageLength), ephemeral: true).ConfigureAwait(false);
            }
        }

        [Command("mods")]
        [Description("List top bot mods giving warnings")]
        public static async ValueTask Mods(
            SlashCommandContext ctx,
            [Description("Number of items to show. Default is 10")]
            int number = 10
        )
        {
            var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
            await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            try
            {
                if (number < 1)
                    number = 10;
                var table = new AsciiTable(
                    new AsciiColumn("Username", maxWidth: 24),
                    new AsciiColumn("Issuer ID", disabled: !ctx.Channel.IsPrivate, alignToRight: true),
                    new AsciiColumn("Warnings given", alignToRight: true),
                    new AsciiColumn("Including retracted", alignToRight: true)
                );
                await using var db = BotDb.OpenRead();
                var query = from warn in db.Warning.AsEnumerable()
                    group warn by warn.IssuerId
                    into modGroup
                    let row = new {userId = modGroup.Key, count = modGroup.Count(w => !w.Retracted), total = modGroup.Count()}
                    orderby row.count descending
                    select row;
                foreach (var row in query.Take(number))
                {
                    var username = await ctx.GetUserNameAsync(row.userId).ConfigureAwait(false);
                    if (username is null or "")
                        username = "Unknown";
                    table.Add(username, row.userId.ToString(), row.count.ToString(), row.total.ToString());
                }
                var pages = AutosplitResponseHelper.AutosplitMessage(new StringBuilder("Warnings issued per bot mod:").Append(table).ToString());
                await ctx.RespondAsync(pages[0], ephemeral: ephemeral).ConfigureAwait(false);
                foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                    await ctx.FollowupAsync(page, ephemeral: ephemeral).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to execute the command: {e.Message}".Trim(EmbedPager.MaxMessageLength), ephemeral: true).ConfigureAwait(false);
            }
        }

        [Command("by")]
        [Description("Shows warnings issued by the specified moderator")]
        public static async ValueTask By(
            SlashCommandContext ctx,
            DiscordUser moderator,
            [Description("Number of items to show. Default is 10")] int number = 10
        )
        {
            await ctx.DeferResponseAsync(true).ConfigureAwait(false);
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
            await using var db = BotDb.OpenRead();
            var query = from warn in db.Warning
                where warn.IssuerId == moderator.Id && !warn.Retracted
                orderby warn.Id descending
                select warn;
            foreach (var row in query.Take(number))
            {
                var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                var timestamp = row.Timestamp.HasValue ? new DateTime(row.Timestamp.Value, DateTimeKind.Utc).ToString("u") : "";
                table.Add(row.Id.ToString(), username, row.DiscordId.ToString(), timestamp, row.Reason, row.FullReason);
            }
            var modName = moderator.Username;
            var pages = AutosplitResponseHelper.AutosplitMessage(new StringBuilder($"Recent warnings issued by {modName}:").Append(table).ToString());
            await ctx.RespondAsync(pages[0], ephemeral: true).ConfigureAwait(false);
            foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                await ctx.FollowupAsync(page, ephemeral: true).ConfigureAwait(false);
        }

        [Command("recent")]
        [Description("Show last issued warnings")]
        public static async ValueTask Last(
            SlashCommandContext ctx,
            [Description("Number of items to show. Default is 10")]
            int number = 10
        )
        {
            await ctx.DeferResponseAsync(true).ConfigureAwait(false);
            var isMod = await ctx.User.IsWhitelistedAsync(ctx.Client, ctx.Guild).ConfigureAwait(false);
            if (number < 1)
                number = 10;
            var table = new AsciiTable(
                new AsciiColumn("ID", alignToRight: true),
                new AsciiColumn("±", disabled: !isMod),
                new AsciiColumn("Username", maxWidth: 24),
                new AsciiColumn("User ID", disabled: !isMod, alignToRight: true),
                new AsciiColumn("Issued by", maxWidth: 15),
                new AsciiColumn("On date (UTC)"),
                new AsciiColumn("Reason"),
                new AsciiColumn("Context", disabled: !isMod)
            );
            await using var db = BotDb.OpenRead();
            IOrderedQueryable<Warning> query;
            if (isMod)
                query = from warn in db.Warning
                    orderby warn.Id descending
                    select warn;
            else
                query = from warn in db.Warning
                    where !warn.Retracted
                    orderby warn.Id descending
                    select warn;
            foreach (var row in query.Take(number))
            {
                var username = await ctx.GetUserNameAsync(row.DiscordId).ConfigureAwait(false);
                var modName = await ctx.GetUserNameAsync(row.IssuerId, defaultName: "Unknown mod").ConfigureAwait(false);
                var timestamp = row.Timestamp.HasValue ? new DateTime(row.Timestamp.Value, DateTimeKind.Utc).ToString("u") : "";
                if (row.Retracted)
                {
                    var modNameRetracted = row.RetractedBy.HasValue ? await ctx.GetUserNameAsync(row.RetractedBy.Value, defaultName: "Unknown mod").ConfigureAwait(false) : "";
                    var timestampRetracted = row.RetractionTimestamp.HasValue ? new DateTime(row.RetractionTimestamp.Value, DateTimeKind.Utc).ToString("u") : "";
                    table.Add(row.Id.ToString(), "-", username, row.DiscordId.ToString(), modNameRetracted, timestampRetracted, row.RetractionReason ?? "", "");
                    table.Add(row.Id.ToString(), "+", username.StrikeThrough(), row.DiscordId.ToString().StrikeThrough(), modName.StrikeThrough(), timestamp.StrikeThrough(), row.Reason.StrikeThrough(), row.FullReason.StrikeThrough());
                }
                else
                    table.Add(row.Id.ToString(), "+", username, row.DiscordId.ToString(), modName, timestamp, row.Reason, row.FullReason);
            }
            var pages = AutosplitResponseHelper.AutosplitMessage(new StringBuilder("Recent warnings:").Append(table).ToString());
            await ctx.RespondAsync(pages[0], ephemeral: true).ConfigureAwait(false);
            foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                await ctx.FollowupAsync(page, ephemeral: true).ConfigureAwait(false);
        }

        private static async ValueTask<bool> CheckListPermissionAsync(SlashCommandContext ctx, ulong userId)
        {
            if (userId == ctx.User.Id || ModProvider.IsMod(ctx.User.Id))
                return true;

            await ctx.RespondAsync($"{Config.Reactions.Denied} You can only view your own warnings").ConfigureAwait(false);
            return false;
        }
    }
}
