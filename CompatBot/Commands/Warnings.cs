using CompatApiClient.Utils;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("warn")]
[Description("Command used to manage warnings")]
internal sealed partial class Warnings
{
    [DefaultGroupCommand] //attributes on overloads do not work, so no easy permission checks
    [Description("Command used to issue a new warning")]
    public async Task Warn(CommandContext ctx, [Description("User to warn. Can also use @id")] DiscordUser user, [RemainingText, Description("Warning explanation")] string reason)
    {
        //need to do manual check of the attribute in all GroupCommand overloads :(
        if (!await new RequiresBotModRoleAttribute().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
            return;

        if (await AddAsync(ctx, user.Id, user.Username.Sanitize(), ctx.Message.Author, reason).ConfigureAwait(false))
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't save the warning, please try again").ConfigureAwait(false);
    }

    [DefaultGroupCommand]
    public async Task Warn(CommandContext ctx, [Description("ID of a user to warn")] ulong userId, [RemainingText, Description("Warning explanation")] string reason)
    {
        if (!await new RequiresBotModRoleAttribute().ExecuteCheckAsync(ctx, false).ConfigureAwait(false))
            return;

        if (await AddAsync(ctx, userId, $"<@{userId}>", ctx.Message.Author, reason).ConfigureAwait(false))
            await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't save the warning, please try again").ConfigureAwait(false);
    }

    [Command("edit"), RequiresBotModRole]
    [Description("Edit specified warning")]
    public async Task Edit(CommandContext ctx, [Description("Warning ID to edit")] int id)
    {
        var interact = ctx.Client.GetInteractivity();
        await using var db = new BotDb();
        var warnings = await db.Warning.Where(w => id.Equals(w.Id)).ToListAsync().ConfigureAwait(false);
        if (warnings.Count == 0)
        {
            await ctx.ReactWithAsync(Config.Reactions.Denied, $"{ctx.Message.Author.Mention} Warn not found", true);
            return;
        }

        var warningToEdit = warnings.First();
        if (warningToEdit.IssuerId != ctx.User.Id)
        {
            await ctx.ReactWithAsync(Config.Reactions.Denied, $"{ctx.Message.Author.Mention} This warn wasn't issued by you :(", true);
            return;
        }

        var msg = await ctx.Channel.SendMessageAsync("Updated warn reason?").ConfigureAwait(false);
        var response = await interact.WaitForMessageAsync(
            m => m.Author == ctx.User
                 && m.Channel == ctx.Channel
                 && !string.IsNullOrEmpty(m.Content)
        ).ConfigureAwait(false);

        await msg.DeleteAsync().ConfigureAwait(false);

        if (string.IsNullOrEmpty(response.Result?.Content))
        {
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Can't edit warning without a new reason").ConfigureAwait(false);
            return;
        }

        warningToEdit.Reason = response.Result.Content;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.Channel.SendMessageAsync($"Warning successfully edited!").ConfigureAwait(false);
    }

    [Command("remove"), TextAlias("delete", "del"), RequiresBotModRole]
    [Description("Removes specified warnings")]
    public async Task Remove(CommandContext ctx, [Description("Warning IDs to remove separated with space")] params int[] ids)
    {
        var interact = ctx.Client.GetInteractivity();
        var msg = await ctx.Channel.SendMessageAsync("What is the reason for removal?").ConfigureAwait(false);
        var response = await interact.WaitForMessageAsync(
            m => m.Author == ctx.User
                 && m.Channel == ctx.Channel
                 && !string.IsNullOrEmpty(m.Content)
        ).ConfigureAwait(false);
        if (string.IsNullOrEmpty(response.Result?.Content))
        {
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Can't remove warnings without a reason").ConfigureAwait(false);
            return;
        }

        await msg.DeleteAsync().ConfigureAwait(false);
        await using var db = new BotDb();
        var warningsToRemove = await db.Warning.Where(w => ids.Contains(w.Id)).ToListAsync().ConfigureAwait(false);
        foreach (var w in warningsToRemove)
        {
            w.Retracted = true;
            w.RetractedBy = ctx.User.Id;
            w.RetractionReason = response.Result.Content;
            w.RetractionTimestamp = DateTime.UtcNow.Ticks;
        }
        var removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
        if (removedCount == ids.Length)
            await ctx.Channel.SendMessageAsync($"Warning{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
        else
            await ctx.Channel.SendMessageAsync($"Removed {removedCount} items, but was asked to remove {ids.Length}").ConfigureAwait(false);
    }

    [Command("clear"), RequiresBotModRole]
    [Description("Removes **all** warnings for a user")]
    public Task Clear(CommandContext ctx, [Description("User to clear warnings for")] DiscordUser user)
        => Clear(ctx, user.Id);

    [Command("clear"), RequiresBotModRole]
    public async Task Clear(CommandContext ctx, [Description("User ID to clear warnings for")] ulong userId)
    {
        var interact = ctx.Client.GetInteractivity();
        var msg = await ctx.Channel.SendMessageAsync("What is the reason for removing all the warnings?").ConfigureAwait(false);
        var response = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
        if (string.IsNullOrEmpty(response.Result?.Content))
        {
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Can't remove warnings without a reason").ConfigureAwait(false);
            return;
        }

        await msg.DeleteAsync().ConfigureAwait(false);
        try
        {
            await using var db = new BotDb();
            var warningsToRemove = await db.Warning.Where(w => w.DiscordId == userId && !w.Retracted).ToListAsync().ConfigureAwait(false);
            foreach (var w in warningsToRemove)
            {
                w.Retracted = true;
                w.RetractedBy = ctx.User.Id;
                w.RetractionReason = response.Result.Content;
                w.RetractionTimestamp = DateTime.UtcNow.Ticks;
            }
            var removed = await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.Channel.SendMessageAsync($"{removed} warning{StringUtils.GetSuffix(removed)} successfully removed!").ConfigureAwait(false);
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
        await using var db = new BotDb();
        var warn = await db.Warning.FirstOrDefaultAsync(w => w.Id == id).ConfigureAwait(false);
        if (warn is { Retracted: true })
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

    internal static async Task<bool> AddAsync(CommandContext ctx, ulong userId, string userName, DiscordUser issuer, string? reason, string? fullReason = null)
    {
        reason = await Sudo.Fix.FixChannelMentionAsync(ctx, reason).ConfigureAwait(false);
        return await AddAsync(ctx.Client, ctx.Message, userId, userName, issuer, reason, fullReason);
    }

    internal static async Task<bool> AddAsync(DiscordClient client, DiscordMessage message, ulong userId, string userName, DiscordUser issuer, string? reason, string? fullReason = null)
    {
        if (string.IsNullOrEmpty(reason))
        {
            var interact = client.GetInteractivity();
            var msg = await message.Channel.SendMessageAsync("What is the reason for this warning?").ConfigureAwait(false);
            var response = await interact.WaitForMessageAsync(
                m => m.Author == message.Author
                     && m.Channel == message.Channel
                     && !string.IsNullOrEmpty(m.Content)
            ).ConfigureAwait(false);
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
            await using var db = new BotDb();
            await db.Warning.AddAsync(new Warning { DiscordId = userId, IssuerId = issuer.Id, Reason = reason, FullReason = fullReason ?? "", Timestamp = DateTime.UtcNow.Ticks }).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);

            var threshold = DateTime.UtcNow.AddMinutes(-15).Ticks;
            var recentCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted && w.Timestamp > threshold);
            if (recentCount > 3)
            {
                Config.Log.Debug("Suicide behavior detected, not spamming with warning responses");
                return true;
            }

            var totalCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted);
            await message.Channel.SendMessageAsync($"User warning saved! User currently has {totalCount} warning{StringUtils.GetSuffix(totalCount)}!").ConfigureAwait(false);
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
            var isWhitelisted = (await client.GetMemberAsync(message.Author).ConfigureAwait(false))?.IsWhitelisted() is true;
            if (message.Author.Id != userId && !isWhitelisted)
            {
                Config.Log.Error($"Somehow {message.Author.Username} ({message.Author.Id}) triggered warning list for {userId}");
                return;
            }

            var channel = message.Channel;
            var isPrivate = channel.IsPrivate;
            int count, removed;
            bool isKot, isDoggo;
            await using var db = new BotDb();
            count = await db.Warning.CountAsync(w => w.DiscordId == userId && !w.Retracted).ConfigureAwait(false);
            removed = await db.Warning.CountAsync(w => w.DiscordId == userId && w.Retracted).ConfigureAwait(false);
            isKot = db.Kot.Any(k => k.UserId == userId);
            isDoggo = db.Doggo.Any(d => d.UserId == userId);
            if (count == 0)
            {
                if (isKot && isDoggo)
                {
                    if (new Random().NextDouble() < 0.5)
                        isKot = false;
                    else
                        isDoggo = false;
                }
                var msg = (removed, isPrivate, isKot, isDoggo) switch
                {
                    (0,    _,  true, false) => $"{userName} has no warnings, is an upstanding kot, and a paw bean of this community",
                    (0,    _, false,  true) => $"{userName} has no warnings, is a good boy, and a wiggling tail of this community",
                    (0,    _,     _,     _) => $"{userName} has no warnings, is an upstanding citizen, and a pillar of this community",
                    (_, true,     _,     _) => $"{userName} has no warnings ({removed} retracted warning{(removed == 1 ? "" : "s")})",
                    (_,    _,  true, false) => $"{userName} has no warnings, but are they a good kot?",
                    (_,    _, false,  true) => $"{userName} has no warnings, but are they a good boy?",
                    _ => $"{userName} has no warnings",
                };
                await message.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                if (!isPrivate || removed == 0)
                    return;
            }

            if (count == 1 && skipIfOne)
                return;

            const int maxWarningsInPublicChannel = 3;
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
                        var retractedByName = warning.RetractedBy.HasValue
                            ? await client.GetUserNameAsync(channel, warning.RetractedBy.Value, isPrivate, "unknown mod").ConfigureAwait(false)
                            : "";
                        var retractionTimestamp = warning.RetractionTimestamp.HasValue
                            ? new DateTime(warning.RetractionTimestamp.Value, DateTimeKind.Utc).ToString("u")
                            : "";
                        table.Add(warning.Id.ToString(), "-", retractedByName, retractionTimestamp, warning.RetractionReason ?? "", "");

                        var issuerName = warning.IssuerId == 0
                            ? ""
                            : await client.GetUserNameAsync(channel, warning.IssuerId, isPrivate, "unknown mod").ConfigureAwait(false);
                        var timestamp = warning.Timestamp.HasValue
                            ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                            : "";
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
                        : "";
                    table.Add(warning.Id.ToString(), "+", issuerName, timestamp, warning.Reason, warning.FullReason);
                }
            }
            
            var result = new StringBuilder("Warning list for ").Append(Formatter.Sanitize(userName));
            if (!isPrivate && !isWhitelisted && count > maxWarningsInPublicChannel)
                result.Append($" (last {showCount} of {count}, full list in DMs)");
            result.AppendLine(":").Append(table);
            await channel.SendAutosplitMessageAsync(result).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }
}