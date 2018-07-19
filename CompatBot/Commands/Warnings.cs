using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using CompatBot.Attributes;
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
    internal sealed partial class Warnings: BaseCommandModule
    {
        [GroupCommand] //attributes on overloads do not work, so no easy permission checks
        [Description("Command used to issue a new warning")]
        public async Task Warn(CommandContext ctx, [Description("User to warn. Can also use @id")] DiscordUser user, [RemainingText, Description("Warning explanation")] string reason)
        {
            //need to do manual check of the attribute in all GroupCommand overloads :(
            if (!await new RequiresBotModRole().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
                return;

            var typingTask = ctx.TriggerTypingAsync();
            if (await AddAsync(ctx.Client, ctx.Message, user.Id, user.Username.Sanitize(), ctx.Message.Author, reason).ConfigureAwait(false))
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            else
                await ctx.Message.CreateReactionAsync(Config.Reactions.Failure).ConfigureAwait(false);
            await typingTask;
        }

        [GroupCommand, RequiresBotModRole]
        public async Task Warn(CommandContext ctx, [Description("ID of a user to warn")] ulong userId, [RemainingText, Description("Warning explanation")] string reason)
        {
            if (!await new RequiresBotModRole().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
                return;

            var typingTask = ctx.TriggerTypingAsync();
            if (await AddAsync(ctx.Client, ctx.Message, userId, $"<@{userId}>", ctx.Message.Author, reason).ConfigureAwait(false))
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            else
                await ctx.Message.CreateReactionAsync(Config.Reactions.Failure).ConfigureAwait(false);
            await typingTask;
        }

        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes specified warnings")]
        public async Task Remove(CommandContext ctx, [Description("Warning IDs to remove separated with space")] params int[] ids)
        {
            var typingTask = ctx.TriggerTypingAsync();
            var warningsToRemove = await BotDb.Instance.Warning.Where(w => ids.Contains(w.Id)).ToListAsync().ConfigureAwait(false);
            BotDb.Instance.Warning.RemoveRange(warningsToRemove);
            var removedCount = await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
            (DiscordEmoji reaction, string msg) result = removedCount == ids.Length
                ? (Config.Reactions.Success, $"Warning{(ids.Length == 1 ? "" : "s")} successfully removed!")
                : (Config.Reactions.Failure, $"Removed {removedCount} items, but was asked to remove {ids.Length}");
            await Task.WhenAll(
                ctx.RespondAsync(result.msg),
                ctx.Message.CreateReactionAsync(result.reaction),
                typingTask
            ).ConfigureAwait(false);
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
                var typingTask = ctx.TriggerTypingAsync();
                //var removed = await BotDb.Instance.Database.ExecuteSqlCommandAsync($"DELETE FROM `warning` WHERE `discord_id`={userId}").ConfigureAwait(false);
                var warningsToRemove = await BotDb.Instance.Warning.Where(w => w.DiscordId == userId).ToListAsync().ConfigureAwait(false);
                BotDb.Instance.Warning.RemoveRange(warningsToRemove);
                var removed = await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                await Task.WhenAll(
                    ctx.RespondAsync($"{removed} warning{(removed == 1 ? "" : "s")} successfully removed!"),
                    ctx.Message.CreateReactionAsync(Config.Reactions.Success),
                    typingTask
                ).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                ctx.Client.DebugLogger.LogMessage(LogLevel.Error, "", e.ToString(), DateTime.Now);
            }

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
                await BotDb.Instance.Warning.AddAsync(new Warning {DiscordId = userId, IssuerId = issuer.Id, Reason = reason, FullReason = fullReason ?? "", Timestamp = DateTime.UtcNow.Ticks}).ConfigureAwait(false);
                await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                var count = await BotDb.Instance.Warning.CountAsync(w => w.DiscordId == userId).ConfigureAwait(false);
                await message.RespondAsync($"User warning saved! User currently has {count} warning{(count % 10 == 1 && count % 100 != 11 ? "" : "s")}!").ConfigureAwait(false);
                if (count > 1)
                    await ListUserWarningsAsync(client, message, userId, userName).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                client.DebugLogger.LogMessage(LogLevel.Error, "", "Couldn't save the warning: " + e, DateTime.Now);
                return false;
            }
        }

        //note: be sure to pass a sanitized userName
        private static async Task ListUserWarningsAsync(DiscordClient client, DiscordMessage message, ulong userId, string userName, bool skipIfOne = true)
        {
            await message.Channel.TriggerTypingAsync().ConfigureAwait(false);
            var count = await BotDb.Instance.Warning.CountAsync(w => w.DiscordId == userId).ConfigureAwait(false);
            if (count == 0)
            {
                await message.RespondAsync(userName + " has no warnings, is a standup citizen, and a pillar of this community").ConfigureAwait(false);
                return;
            }

            if (count == 1 && skipIfOne)
                return;

            var isPrivate = message.Channel.IsPrivate;
            var result = new StringBuilder("Warning list for ").Append(userName).AppendLine(":")
                .AppendLine("```");
            var header = $"{"ID",-5} | {"Issued by",-15} | {"On date (UTC)",-20} | Reason";
            if (isPrivate)
                header += "          | Full reason";
            result.AppendLine(header)
                  .AppendLine("".PadLeft(header.Length, '-'));
            foreach (var warning in BotDb.Instance.Warning.Where(w => w.DiscordId == userId))
            {
                var issuerName = warning.IssuerId == 0 ? "" : await client.GetUserNameAsync(message.Channel, warning.IssuerId, isPrivate, "unknown mod").ConfigureAwait(false);
                var timestamp = warning.Timestamp.HasValue ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u") : null;
                result.Append($"{warning.Id:00000} | {issuerName,-15} | {timestamp,-20} | {warning.Reason}");
                if (isPrivate)
                    result.Append(" | ").Append(warning.FullReason);
                result.AppendLine();
            }
            await message.Channel.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
        }
    }
}
