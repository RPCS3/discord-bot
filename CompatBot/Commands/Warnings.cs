using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
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
    [Group("warn")]
    [Description("Command used to manage warnings")]
    internal sealed partial class Warnings: BaseCommandModuleCustom
    {
        [GroupCommand] //attributes on overloads do not work, so no easy permission checks
        [Description("Command used to issue a new warning")]
        public async Task Warn(CommandContext ctx, [Description("User to warn. Can also use @id")] DiscordUser user, [RemainingText, Description("Warning explanation")] string reason)
        {
            //need to do manual check of the attribute in all GroupCommand overloads :(
            if (!await new RequiresBotModRole().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
                return;

            if (await AddAsync(ctx, user.Id, user.Username.Sanitize(), ctx.Message.Author, reason).ConfigureAwait(false))
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't save the warning, please try again").ConfigureAwait(false);
        }

        [GroupCommand, RequiresBotModRole]
        public async Task Warn(CommandContext ctx, [Description("ID of a user to warn")] ulong userId, [RemainingText, Description("Warning explanation")] string reason)
        {
            if (!await new RequiresBotModRole().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
                return;

            if (await AddAsync(ctx, userId, $"<@{userId}>", ctx.Message.Author, reason).ConfigureAwait(false))
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't save the warning, please try again").ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes specified warnings")]
        public async Task Remove(CommandContext ctx, [Description("Warning IDs to remove separated with space")] params int[] ids)
        {
            int removedCount;
            using (var db = new BotDb())
            {
                var warningsToRemove = await db.Warning.Where(w => ids.Contains(w.Id)).ToListAsync().ConfigureAwait(false);
                db.Warning.RemoveRange(warningsToRemove);
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            if (removedCount == ids.Length)
                await ctx.RespondAsync($"Warning{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            else
                await ctx.RespondAsync($"Removed {removedCount} items, but was asked to remove {ids.Length}").ConfigureAwait(false);
        }

        [Command("clear"), RequiresBotModRole]
        [Description("Removes **all** warings for a user")]
        public Task Clear(CommandContext ctx, [Description("User to clear warnings for")] DiscordUser user)
        {
            return Clear(ctx, user.Id);
        }

        [Command("clear"), RequiresBotModRole]
        public async Task Clear(CommandContext ctx, [Description("User ID to clear warnings for")] ulong userId)
        {
            try
            {
                //var removed = await BotDb.Instance.Database.ExecuteSqlCommandAsync($"DELETE FROM `warning` WHERE `discord_id`={userId}").ConfigureAwait(false);
                int removed;
                using (var db = new BotDb())
                {
                    var warningsToRemove = await db.Warning.Where(w => w.DiscordId == userId).ToListAsync().ConfigureAwait(false);
                    db.Warning.RemoveRange(warningsToRemove);
                    removed = await db.SaveChangesAsync().ConfigureAwait(false);
                }
                await ctx.RespondAsync($"{removed} warning{StringUtils.GetSuffix(removed)} successfully removed!").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        internal static async Task<bool> AddAsync(CommandContext ctx, ulong userId, string userName, DiscordUser issuer, string reason, string fullReason = null)
        {
            reason = await Sudo.Fix.FixChannelMentionAsync(ctx, reason).ConfigureAwait(false);
            return await AddAsync(ctx.Client, ctx.Message, userId, userName, issuer, reason, fullReason);
        }

        internal static async Task<bool> AddAsync(DiscordClient client, DiscordMessage message, ulong userId, string userName, DiscordUser issuer, string reason, string fullReason = null)
        {
            if (string.IsNullOrEmpty(reason))
            {
                await message.RespondAsync("A reason needs to be provided").ConfigureAwait(false);
                return false;
            }
            try
            {
                int totalCount;
                using (var db = new BotDb())
                {
                    await db.Warning.AddAsync(new Warning { DiscordId = userId, IssuerId = issuer.Id, Reason = reason, FullReason = fullReason ?? "", Timestamp = DateTime.UtcNow.Ticks }).ConfigureAwait(false);
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    var threshold = DateTime.UtcNow.AddMinutes(-15).Ticks;
                    var recentCount = db.Warning.Count(w => w.DiscordId == userId && w.Timestamp > threshold);
                    if (recentCount > 3)
                    {
                        Config.Log.Debug("Suicide behavior detected, not spamming with warning responses");
                        return true;
                    }

                    totalCount = db.Warning.Count(w => w.DiscordId == userId);
                }
                await message.RespondAsync($"User warning saved! User currently has {totalCount} warning{StringUtils.GetSuffix(totalCount)}!").ConfigureAwait(false);
                if (totalCount > 1)
                    await ListUserWarningsAsync(client, message, userId, userName).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Couldn't save the warning");
                return false;
            }
        }

        //note: be sure to pass a sanitized userName
        private static async Task ListUserWarningsAsync(DiscordClient client, DiscordMessage message, ulong userId, string userName, bool skipIfOne = true)
        {
            try
            {
                var channel = message.Channel;
                int count;
                using (var db = new BotDb())
                    count = await db.Warning.CountAsync(w => w.DiscordId == userId).ConfigureAwait(false);
                if (count == 0)
                {
                    await message.RespondAsync(userName + " has no warnings, is a standup citizen, and a pillar of this community").ConfigureAwait(false);
                    return;
                }

                if (count == 1 && skipIfOne)
                    return;

                const int maxWarningsInPublicChannel = 3;
                var isPrivate = channel.IsPrivate;
                var isWhitelisted = client.GetMember(message.Author)?.IsWhitelisted() ?? false;
                using (var db = new BotDb())
                {
                    var totalWarningCount = db.Warning.Count(w => w.DiscordId == userId);
                    var showCount = Math.Min(maxWarningsInPublicChannel, totalWarningCount);
                    var result = new StringBuilder("Warning list for ").Append(userName);
                    if (!isPrivate && !isWhitelisted && totalWarningCount > maxWarningsInPublicChannel)
                        result.Append($" (last {showCount} of {totalWarningCount}, full list in DMs)");
                    result.AppendLine(":").AppendLine("```");
                    var header = $"{"ID",-5} | {"Issued by",-15} | {"On date (UTC)",-20} | Reason";
                    if (isPrivate)
                        header += "          | Full reason";
                    result.AppendLine(header)
                        .AppendLine("".PadLeft(header.Length, '-'));
                    IQueryable<Warning> query = db.Warning.Where(w => w.DiscordId == userId).OrderByDescending(w => w.Id);
                    if (!isPrivate && !isWhitelisted)
                        query = query.Take(maxWarningsInPublicChannel);
                    foreach (var warning in await query.ToListAsync().ConfigureAwait(false))
                    {
                        var issuerName = warning.IssuerId == 0
                            ? ""
                            : await client.GetUserNameAsync(channel, warning.IssuerId, isPrivate, "unknown mod").ConfigureAwait(false);
                        var timestamp = warning.Timestamp.HasValue
                            ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                            : null;
                        result.Append($"{warning.Id:00000} | {issuerName,-15} | {timestamp,-20} | {warning.Reason}");
                        if (isPrivate)
                            result.Append(" | ").Append(warning.FullReason);
                        result.AppendLine();
                    }
                    await channel.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }
    }
}
