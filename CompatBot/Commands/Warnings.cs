using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("warning"), RequiresBotModRole, AllowDMUsage]
[Description("Command used to manage warnings")]
internal static partial class Warnings
{
    [Command("give")]
    [Description("Issue a new warning to a user")]
    public static async ValueTask Warn(
        SlashCommandContext ctx,
        [Description("User to warn")]
        DiscordUser user,
        [Description("Warning explanation")]
        string reason
    )
    {
        await ctx.DeferResponseAsync(ephemeral: true).ConfigureAwait(false);
        var (saved, suppress, recent, total) = await AddAsync(user.Id, ctx.User, reason).ConfigureAwait(false);
        if (!saved)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Couldn't save the warning, please try again", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!suppress)
        {
            var userMsgContent = $"""
                  User warning saved, {user.Mention} has {recent} recent warning{StringUtils.GetSuffix(recent)} ({total} total)
                  Warned for: {reason}
                  """;
            var userMsg = new DiscordMessageBuilder()
                .WithContent(userMsgContent)
                .AddMention(UserMention.All);
            await ctx.Channel.SendMessageAsync(userMsg).ConfigureAwait(false);
        }
        await ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize()).ConfigureAwait(false);
    }

    [Command("update")]
    [Description("Change warning details")]
    public static async ValueTask Edit(
        SlashCommandContext ctx,
        [Description("Warning ID to edit"), SlashAutoCompleteProvider<WarningAutoCompleteProvider>]
        int id,
        [Description("Updated warning explanation")]
        string reason,
        [Description("User to filter autocomplete results")]
        DiscordUser? user = null
    )
    {
        await using var db = new BotDb();
        var warnings = await db.Warning.Where(w => id.Equals(w.Id)).ToListAsync().ConfigureAwait(false);
        if (warnings.Count is 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Warning not found", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var warningToEdit = warnings.First();
        if (warningToEdit.IssuerId != ctx.User.Id)
        {
            await ctx.RespondAsync($"{Config.Reactions.Denied} This warning wasn't issued by you", ephemeral: true).ConfigureAwait(false);
            return;
        }

        warningToEdit.Reason = reason;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync("Warning successfully updated", ephemeral: true).ConfigureAwait(false);
    }

    [Command("remove")]
    [Description("Removes specified warnings")]
    public static async ValueTask Remove(
        SlashCommandContext ctx,
        [Description("Warning ID to remove"), SlashAutoCompleteProvider<WarningAutoCompleteProvider>]
        int id,
        [Description("Reason for warning removal")]
        string reason,
        [Description("User to filter autocomplete results")]
        DiscordUser? user = null
    )
    {
        await using var db = new BotDb();
        var warningsToRemove = await db.Warning.Where(w => w.Id == id).ToListAsync().ConfigureAwait(false);
        foreach (var w in warningsToRemove)
        {
            w.Retracted = true;
            w.RetractedBy = ctx.User.Id;
            w.RetractionReason = reason;
            w.RetractionTimestamp = DateTime.UtcNow.Ticks;
        }
        var removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
        if (removedCount is 0)
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to remove warning").ConfigureAwait(false);
        else
        {
            await ctx.Channel.SendMessageAsync("Warning successfully removed").ConfigureAwait(false);
            user ??= await ctx.Client.GetUserAsync(warningsToRemove[0].DiscordId).ConfigureAwait(false);
            await ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize(), false).ConfigureAwait(false);
        }
    }

    [Command("clear")]
    [Description("Removes **all** warnings for a user")]
    public static async ValueTask Clear(
        SlashCommandContext ctx,
        [Description("User to clear warnings for")]
        DiscordUser user,
        [Description("Reason for clear warning removal")]
        string reason
    )
    {
        try
        {
            await using var db = new BotDb();
            var warningsToRemove = await db.Warning.Where(w => w.DiscordId == user.Id && !w.Retracted).ToListAsync().ConfigureAwait(false);
            foreach (var w in warningsToRemove)
            {
                w.Retracted = true;
                w.RetractedBy = ctx.User.Id;
                w.RetractionReason = reason;
                w.RetractionTimestamp = DateTime.UtcNow.Ticks;
            }
            var removed = await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.Channel.SendMessageAsync($"{removed} warning{StringUtils.GetSuffix(removed)} successfully removed!").ConfigureAwait(false);
            await ListUserWarningsAsync(ctx.Client, ctx.Interaction, user.Id, user.Username.Sanitize()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    [Command("revert")]
    [Description("Bring back warning that's been removed before")]
    public static async ValueTask Revert(
        SlashCommandContext ctx,
        [Description("Warning ID to change"), SlashAutoCompleteProvider<WarningAutoCompleteProvider>]
        int id,
        [Description("User to filter autocomplete results")]
        DiscordUser? user = null
    )
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
            await ctx.RespondAsync($"{Config.Reactions.Success} Reissued the warning", ephemeral: true).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Warning is not retracted", ephemeral: true).ConfigureAwait(false);
    }

    internal static async ValueTask<(bool saved, bool suppress, int recentCount, int totalCount)>
        AddAsync(ulong userId, DiscordUser issuer, string reason, string? fullReason = null)
    {
        try
        {
            await using var db = new BotDb();
            await db.Warning.AddAsync(
                new()
                {
                    DiscordId = userId,
                    IssuerId = issuer.Id,
                    Reason = reason,
                    FullReason = fullReason ?? "",
                    Timestamp = DateTime.UtcNow.Ticks
                }
            ).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);

            var threshold = DateTime.UtcNow.AddMinutes(-15).Ticks;
            var totalCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted);
            var recentCount = db.Warning.Count(w => w.DiscordId == userId && !w.Retracted && w.Timestamp > threshold);
            if (recentCount < 4)
                return (true, false, recentCount, totalCount);
            
            Config.Log.Debug("Suicide behavior detected, not spamming with warning responses");
            return (true, true, recentCount, totalCount);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Couldn't save the warning");
            return default;
        }
    }

    //note: be sure to pass a sanitized userName
    //note2: itneraction must be deferred
    internal static async ValueTask ListUserWarningsAsync(DiscordClient client, DiscordInteraction interaction, ulong userId, string userName, bool skipIfOne = true, bool useFollowup = false)
    {
        try
        {
            var isMod = await interaction.User.IsWhitelistedAsync(client, interaction.Guild).ConfigureAwait(false);
            if (interaction.User.Id != userId && !isMod)
            {
                Config.Log.Error($"Somehow {interaction.User.Username} ({interaction.User.Id}) triggered warning list for {userId}");
                return;
            }

            const bool ephemeral = true;
            int count, removed;
            bool isKot, isDoggo;
            await using var db = new BotDb();
            count = await db.Warning.CountAsync(w => w.DiscordId == userId && !w.Retracted).ConfigureAwait(false);
            removed = await db.Warning.CountAsync(w => w.DiscordId == userId && w.Retracted).ConfigureAwait(false);
            isKot = db.Kot.Any(k => k.UserId == userId);
            isDoggo = db.Doggo.Any(d => d.UserId == userId);
            var response = new DiscordInteractionResponseBuilder().AsEphemeral(ephemeral);
            if (count is 0)
            {
                if (isKot && isDoggo)
                {
                    if (new Random().NextDouble() < 0.5)
                        isKot = false;
                    else
                        isDoggo = false;
                }
                var msg = (removed, ephemeral, isKot, isDoggo) switch
                {
                    (0,    _,  true, false) => $"{userName} has no warnings, is an upstanding kot, and a paw bean of this community",
                    (0,    _, false,  true) => $"{userName} has no warnings, is a good boy, and a wiggling tail of this community",
                    (0,    _,     _,     _) => $"{userName} has no warnings, is an upstanding citizen, and a pillar of this community",
                    (_, true,     _,     _) => $"{userName} has no warnings ({removed} retracted warning{(removed == 1 ? "" : "s")})",
                    (_,    _,  true, false) => $"{userName} has no warnings, but are they a good kot?",
                    (_,    _, false,  true) => $"{userName} has no warnings, but are they a good boy?",
                    _ => $"{userName} has no warnings",
                };
                ;
                await interaction.EditOriginalResponseAsync(new(response.WithContent(msg))).ConfigureAwait(false);
                if (!ephemeral || removed is 0)
                    return;
            }

            if (count is 1 && skipIfOne)
            {
                await interaction.EditOriginalResponseAsync(new(response.WithContent("No additional warnings on record"))).ConfigureAwait(false);
                return;
            }

            const int maxWarningsInPublicChannel = 3;
            var showCount = Math.Min(maxWarningsInPublicChannel, count);
            var table = new AsciiTable(
                new AsciiColumn("ID", alignToRight: true),
                new AsciiColumn("±", disabled: !ephemeral || !isMod),
                new AsciiColumn("By", maxWidth: 15),
                new AsciiColumn("On date (UTC)"),
                new AsciiColumn("Reason"),
                new AsciiColumn("Context", disabled: !ephemeral || !isMod, maxWidth: 4096)
            );
            IQueryable<Warning> query = db.Warning.Where(w => w.DiscordId == userId).OrderByDescending(w => w.Id);
            if (!ephemeral || !isMod)
                query = query.Where(w => !w.Retracted);
            if (!ephemeral && !isMod)
                query = query.Take(maxWarningsInPublicChannel);
            foreach (var warning in await query.ToListAsync().ConfigureAwait(false))
            {
                if (warning.Retracted)
                {
                    if (isMod && ephemeral)
                    {
                        var retractedByName = warning.RetractedBy.HasValue
                            ? await client.GetUserNameAsync(interaction.Channel, warning.RetractedBy.Value, ephemeral, "unknown mod").ConfigureAwait(false)
                            : "";
                        var retractionTimestamp = warning.RetractionTimestamp.HasValue
                            ? new DateTime(warning.RetractionTimestamp.Value, DateTimeKind.Utc).ToString("u")
                            : "";
                        table.Add(warning.Id.ToString(), "-", retractedByName, retractionTimestamp, warning.RetractionReason ?? "", "");

                        var issuerName = warning.IssuerId is 0
                            ? ""
                            : await client.GetUserNameAsync(interaction.Channel, warning.IssuerId, ephemeral, "unknown mod").ConfigureAwait(false);
                        var timestamp = warning.Timestamp.HasValue
                            ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                            : "";
                        table.Add(warning.Id.ToString().StrikeThrough(), "+", issuerName.StrikeThrough(), timestamp.StrikeThrough(), warning.Reason.StrikeThrough(), warning.FullReason.StrikeThrough());
                    }
                }
                else
                {
                    var issuerName = warning.IssuerId is 0
                        ? ""
                        : await client.GetUserNameAsync(interaction.Channel, warning.IssuerId, ephemeral, "unknown mod").ConfigureAwait(false);
                    var timestamp = warning.Timestamp.HasValue
                        ? new DateTime(warning.Timestamp.Value, DateTimeKind.Utc).ToString("u")
                        : "";
                    table.Add(warning.Id.ToString(), "+", issuerName, timestamp, warning.Reason, warning.FullReason);
                }
            }
            
            var result = new StringBuilder("Warning list for ").Append(Formatter.Sanitize(userName));
            if (!ephemeral && !isMod && count > maxWarningsInPublicChannel)
                result.Append($" (last {showCount} of {count}, full list in DMs)");
            result.AppendLine(":").Append(table);
            var pages = AutosplitResponseHelper.AutosplitMessage(result.ToString());
            await interaction.EditOriginalResponseAsync(new(response.WithContent(pages[0]))).ConfigureAwait(false);
            if (useFollowup)
            {
                foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                {
                    var followupMsg = new DiscordFollowupMessageBuilder()
                        .AsEphemeral(ephemeral)
                        .WithContent(page);
                    await interaction.CreateFollowupMessageAsync(followupMsg).ConfigureAwait(false);
                }
            }        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }
}