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
using DSharpPlus.Interactivity;
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
            var interact = ctx.Client.GetInteractivity();
            var msg = await ctx.RespondAsync("What is the reason for removal?").ConfigureAwait(false);
            var response = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response.Result?.Content))
            {
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Can't remove warnings without a reason").ConfigureAwait(false);
                return;
            }

            await msg.DeleteAsync().ConfigureAwait(false);
            int removedCount;
            using (var db = new BotDb())
            {
                var warningsToRemove = await db.Warning.Where(w => ids.Contains(w.Id)).ToListAsync().ConfigureAwait(false);
                foreach (var w in warningsToRemove)
                {
                    w.Retracted = true;
                    w.RetractedBy = ctx.User.Id;
                    w.RetractionReason = response.Result.Content;
                    w.RetractionTimestamp = DateTime.UtcNow.Ticks;
                }
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            if (removedCount == ids.Length)
                await ctx.RespondAsync($"Warning{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            else
                await ctx.RespondAsync($"Removed {removedCount} items, but was asked to remove {ids.Length}").ConfigureAwait(false);
        }

        [Command("clear"), RequiresBotModRole]
        [Description("Removes **all** warnings for a user")]
        public Task Clear(CommandContext ctx, [Description("User to clear warnings for")] DiscordUser user)
        {
            return Clear(ctx, user.Id);
        }

        [Command("clear"), RequiresBotModRole]
        public async Task Clear(CommandContext ctx, [Description("User ID to clear warnings for")] ulong userId)
        {
            var interact = ctx.Client.GetInteractivity();
            var msg = await ctx.RespondAsync("What is the reason for removing all the warnings?").ConfigureAwait(false);
            var response = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
            if (string.IsNullOrEmpty(response.Result?.Content))
            {
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Can't remove warnings without a reason").ConfigureAwait(false);
                return;
            }

            await msg.DeleteAsync().ConfigureAwait(false);
            try
            {
                int removed;
                using (var db = new BotDb())
                {
                    var warningsToRemove = await db.Warning.Where(w => w.DiscordId == userId).ToListAsync().ConfigureAwait(false);
                    foreach (var w in warningsToRemove)
                    {
                        w.Retracted = true;
                        w.RetractedBy = ctx.User.Id;
                        w.RetractionReason = response.Result.Content;
                        w.RetractionTimestamp = DateTime.UtcNow.Ticks;
                    }
                    removed = await db.SaveChangesAsync().ConfigureAwait(false);
                }
                await ctx.RespondAsync($"{removed} warning{StringUtils.GetSuffix(removed)} successfully removed!").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        [Command("revert"), RequiresBotModRole]
        [Description("Changes the state of the warning status")]
        public async Task Revert(CommandContext ctx, [Description("Warning ID to change")] int id)
        {
            using (var db = new BotDb())
            {
                var warn = await db.Warning.FirstOrDefaultAsync(w => w.Id == id).ConfigureAwait(false);
                if (warn.Retracted)
                {
                    warn.Retracted = false;
                    warn.RetractedBy = null;
                    warn.RetractionReason = null;
                    warn.RetractionTimestamp = null;
                    await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, "Reissued the warning", true).ConfigureAwait(false);
                }
                else
                    await Remove(ctx, id).ConfigureAwait(false);
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
                var interact = client.GetInteractivity();
                var msg = await message.Channel.SendMessageAsync("What is the reason for this warning?").ConfigureAwait(false);
                var response = await interact.WaitForMessageAsync(m => m.Author == message.Author && m.Channel == message.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
                if (string.IsNullOrEmpty(response.Result.Content))
                {
                    await msg.UpdateOrCreateMessageAsync(message.Channel, "A reason needs to be provided").ConfigureAwait(false);
                    return false;
                }
                await msg.DeleteAsync().ConfigureAwait(false);
                reason = response.Result.Content;
            }
            try
            {
                int totalCount;
                using (var db = new BotDb())
                {
                    await db.Warning.AddAsync(new Warning { DiscordId = userId, IssuerId = issuer.Id, Reason = reason, FullReason = fullReason ?? "", Timestamp = DateTime.UtcNow.Ticks }).ConfigureAwait(false);
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    var threshold = DateTime.UtcNow.AddMinutes(-15).Ticks;
                    var recentCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted && w.Timestamp > threshold);
                    if (recentCount > 3)
                    {
                        Config.Log.Debug("Suicide behavior detected, not spamming with warning responses");
                        return true;
                    }

                    totalCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted);
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
                var isWhitelisted = client.GetMember(message.Author)?.IsWhitelisted() ?? false;
                if (message.Author.Id != userId && !isWhitelisted)
                {
                    Config.Log.Error($"Somehow {message.Author.Username} ({message.Author.Id}) triggered warning list for {userId}");
                    return;
                }

                var channel = message.Channel;
                var isPrivate = channel.IsPrivate;
                int count, removed;
                bool isKot;
                using (var db = new BotDb())
                {
                    count = await db.Warning.CountAsync(w => w.DiscordId == userId && !w.Retracted).ConfigureAwait(false);
                    removed = await db.Warning.CountAsync(w => w.DiscordId == userId && w.Retracted).ConfigureAwait(false);
                    isKot = db.Kot.Any(k => k.UserId == userId);
                }
                if (count == 0)
                {
                    if (removed == 0)
                        await message.RespondAsync($"{userName} has no warnings, is a standup {(isKot ? "kot" : "citizen")}, and {(isKot ? "paw beans" : "a pillar")} of this community").ConfigureAwait(false);
                    else
                        await message.RespondAsync(userName + " has no warnings" + (isPrivate ? $" ({removed} retracted warning{(removed == 1 ? "" : "s")})" : "")).ConfigureAwait(false);
                    return;
                }

                if (count == 1 && skipIfOne)
                    return;

                const int maxWarningsInPublicChannel = 3;
                using (var db = new BotDb())
                {
                    var showCount = Math.Min(maxWarningsInPublicChannel, count);
                    var table = new AsciiTable(
                        new AsciiColumn("ID", alignToRight: true),
                        new AsciiColumn("±", disabled: !isPrivate || !isWhitelisted),
                        new AsciiColumn("By", maxWidth: 15),
                        new AsciiColumn("On date (UTC)"),
                        new AsciiColumn("Reason"),
                        new AsciiColumn("Context", disabled: !isPrivate, maxWidth: 4096)
                        );
                    IQueryable<Warning> query = db.Warning.Where(w => w.DiscordId == userId).OrderByDescending(w => w.Id);
                    if (!isPrivate || !isWhitelisted)
                        query = query.Where(w => !w.Retracted);
                    if (!isPrivate && !isWhitelisted)
                        query = query.Take(maxWarningsInPublicChannel);
                    foreach (var warning in await query.ToListAsync().ConfigureAwait(false))
                    {
                        if (warning.Retracted)
                        {
                            if (isWhitelisted && isPrivate)
                            {
                                var retractedByName = !warning.RetractedBy.HasValue
                                    ? ""
                                    : await client.GetUserNameAsync(channel, warning.RetractedBy.Value, isPrivate, "unknown mod").ConfigureAwait(false);
                                var retractionTimestamp = warning.RetractionTimestamp.HasValue
                                    ? new DateTime(warning.RetractionTimestamp.Value, DateTimeKind.Utc).ToString("u")
                                    : null;
                                table.Add(warning.Id.ToString(), "-", retractedByName, retractionTimestamp, warning.RetractionReason, "");

                                var issuerName = warning.IssuerId == 0
                                    ? ""
                                    : await client.GetUserNameAsync(channel, warning.IssuerId, isPrivate, "unknown mod").ConfigureAwait(false);
                                var timestamp = warning.Timestamp.HasValue
                                    ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                                    : null;
                                table.Add(warning.Id.ToString().StrikeThrough(), "+", issuerName.StrikeThrough(), timestamp.StrikeThrough(), warning.Reason.StrikeThrough(), warning.FullReason.StrikeThrough());
                            }
                        }
                        else
                        {
                            var issuerName = warning.IssuerId == 0
                                ? ""
                                : await client.GetUserNameAsync(channel, warning.IssuerId, isPrivate, "unknown mod").ConfigureAwait(false);
                            var timestamp = warning.Timestamp.HasValue
                                ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                                : null;
                            table.Add(warning.Id.ToString(), "+", issuerName, timestamp, warning.Reason, warning.FullReason);
                        }
                    }
                    var result = new StringBuilder("Warning list for ").Append(userName);
                    if (!isPrivate && !isWhitelisted && count > maxWarningsInPublicChannel)
                        result.Append($" (last {showCount} of {count}, full list in DMs)");
                    result.AppendLine(":").Append(table);
                    await channel.SendAutosplitMessageAsync(result).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }
    }
}
