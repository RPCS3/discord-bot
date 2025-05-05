using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("event")]
[Description("Provides information about the various events in the game industry")]
internal static partial class Events
{
    [GeneratedRegex(@"((?<days>\d+)(\.|d\s*))?((?<hours>\d+)(\:|h\s*))?((?<mins>\d+)m?)?", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex Duration();

    [Command("countdown")]
    [Description("Show countdown for the nearest known event")]
    public static async ValueTask Countdown(
        SlashCommandContext ctx,
        [Description("Event or schedule entry name (E3, game release, etc)"), SlashAutoCompleteProvider<EventNameAutoCompleteProvider>]
        string? name = null
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
        var originalEventName = name = name?.Trim(40);
#if DEBUG        
        var current = DateTime.UtcNow.AddYears(-10);
#else
        var current = DateTime.UtcNow;
#endif
        var currentTicks = current.Ticks;
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        var currentEvents = await db.EventSchedule.OrderBy(e => e.End).Where(e => e.Start <= currentTicks && e.End >= currentTicks).ToListAsync().ConfigureAwait(false);
        var nextEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks).ConfigureAwait(false);
        if (string.IsNullOrEmpty(name))
        {
            var nearestEventMsg = "";
            if (currentEvents.Count > 0)
            {
                if (currentEvents.Count == 1)
                    nearestEventMsg = $"Current event: {currentEvents[0].Name} (going for {FormatCountdown(current - currentEvents[0].Start.AsUtc())})\n";
                else
                {
                    nearestEventMsg = "Current events:\n";
                    foreach (var e in currentEvents)
                        nearestEventMsg += $"{e.Name} (going for {FormatCountdown(current - e.Start.AsUtc())})\n";
                }
            }
            if (nextEvent != null)
                nearestEventMsg += $"Next event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
            if (nearestEventMsg is not { Length: > 0 })
                nearestEventMsg = "No known events scheduled";
            await ctx.RespondAsync(nearestEventMsg.TrimEnd(), ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        name = await FuzzyMatchEventName(db, name).ConfigureAwait(false);
        var promo = "";
        if (currentEvents.Count > 0)
            promo = $"\nMeanwhile check out this {(string.IsNullOrEmpty(currentEvents[0].EventName) ? "" : currentEvents[0].EventName + " " + currentEvents[0].Year + " ")}event in progress: {currentEvents[0].Name} (going for {FormatCountdown(current - currentEvents[0].Start.AsUtc())})";
        else if (nextEvent != null)
            promo = $"\nMeanwhile check out this upcoming {(string.IsNullOrEmpty(nextEvent.EventName) ? "" : nextEvent.EventName + " " + nextEvent.Year + " ")}event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
        var firstNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year >= current.Year && e.EventName == name).ConfigureAwait(false);
        if (firstNamedEvent == null)
        {
            var scheduleEntry = await FuzzyMatchEntryName(db, originalEventName).ConfigureAwait(false);
            var events = await db.EventSchedule.OrderBy(e => e.Start).Where(e => e.End > current.Ticks && e.Name == scheduleEntry).ToListAsync().ConfigureAwait(false);
            if (events.Count > 0)
            {
                var eventListMsg = new StringBuilder();
                foreach (var eventEntry in events)
                {
                    if (eventEntry.Start < current.Ticks)
                        eventListMsg.AppendLine($"{eventEntry.Name} ends in {FormatCountdown(eventEntry.End.AsUtc() - current)}");
                    else
                        eventListMsg.AppendLine($"{eventEntry.Name} starts in {FormatCountdown(eventEntry.Start.AsUtc() - current)}");
                }
                //await ctx.SendAutosplitMessageAsync(eventListMsg.ToString(), blockStart: "", blockEnd: "").ConfigureAwait(false);
                var msgList = AutosplitResponseHelper.AutosplitMessage(eventListMsg.ToString(), blockStart: null, blockEnd: null);
                await ctx.RespondAsync(msgList[0], ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }

            var noEventMsg = $"No information about the upcoming {name?.Sanitize(replaceBackTicks: true)} at the moment";
            if (name?.Length > 10)
                noEventMsg = "No information about such event at the moment";
            else if (ctx.User.Id is 259997001880436737ul or 377190919327318018ul)
            {
                noEventMsg = $"Haha, very funny, {ctx.User.Mention}. So original. Never saw this joke before.";
                promo = null;
            }
            if (!string.IsNullOrEmpty(promo))
                noEventMsg += promo;
            await ctx.RespondAsync(noEventMsg, ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (firstNamedEvent.Start >= currentTicks)
        {
            var upcomingNamedEventMsg = $"__{FormatCountdown(firstNamedEvent.Start.AsUtc() - current)} until {name} {firstNamedEvent.Year}!__";
            if (string.IsNullOrEmpty(promo) || nextEvent?.Id == firstNamedEvent.Id)
                upcomingNamedEventMsg += $"\nFirst event: {firstNamedEvent.Name}";
            else
                upcomingNamedEventMsg += promo;
            await ctx.RespondAsync(upcomingNamedEventMsg, ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        var lastNamedEvent = await db.EventSchedule.OrderByDescending(e => e.End).FirstOrDefaultAsync(e => e.Year == current.Year && e.EventName == name).ConfigureAwait(false);
        if (lastNamedEvent is not null && lastNamedEvent.End <= currentTicks)
        {
            if (lastNamedEvent.End < current.AddMonths(-1).Ticks)
            {
                firstNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year >= current.Year + 1 && e.EventName == name).ConfigureAwait(false);
                if (firstNamedEvent == null)
                {
                    var noEventMsg = $"No information about the upcoming {name?.Sanitize(replaceBackTicks: true)} at the moment";
                    if (name?.Length > 10)
                        noEventMsg = "No information about such event at the moment";
                    else if (ctx.User.Id is 259997001880436737ul or 377190919327318018ul)
                    {
                        noEventMsg = $"Haha, very funny, {ctx.User.Mention}. So original. Never saw this joke before.";
                        promo = null;
                    }
                    if (!string.IsNullOrEmpty(promo))
                        noEventMsg += promo;
                    await ctx.RespondAsync(noEventMsg, ephemeral: ephemeral).ConfigureAwait(false);
                    return;
                }
                        
                var upcomingNamedEventMsg = $"__{FormatCountdown(firstNamedEvent.Start.AsUtc() - current)} until {name} {firstNamedEvent.Year}!__";
                if (string.IsNullOrEmpty(promo) || nextEvent?.Id == firstNamedEvent.Id)
                    upcomingNamedEventMsg += $"\nFirst event: {firstNamedEvent.Name}";
                else
                    upcomingNamedEventMsg += promo;
                await ctx.RespondAsync(upcomingNamedEventMsg, ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }

            var e3EndedMsg = $"__{name} {current.Year} has concluded. See you next year! (maybe)__";
            if (!string.IsNullOrEmpty(promo))
                e3EndedMsg += promo;
            await ctx.RespondAsync(e3EndedMsg, ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        var currentNamedEvent = await db.EventSchedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.Start <= currentTicks && e.End >= currentTicks && e.EventName == name).ConfigureAwait(false);
        var nextNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks && e.EventName == name).ConfigureAwait(false);
        var msg = $"__{name} {current.Year} is already in progress!__\n";
        if (currentNamedEvent != null)
            msg += $"Current event: {currentNamedEvent.Name} (going for {FormatCountdown(current - currentNamedEvent.Start.AsUtc())})\n";
        if (nextNamedEvent != null)
            msg += $"Next event: {nextNamedEvent.Name} (starts in {FormatCountdown(nextNamedEvent.Start.AsUtc() - current)})";
        //await ctx.SendAutosplitMessageAsync(msg.TrimEnd(), blockStart: "", blockEnd: "").ConfigureAwait(false);
        var result = AutosplitResponseHelper.AutosplitMessage(msg, blockStart: null, blockEnd: null);
        await ctx.RespondAsync(result[0], ephemeral: ephemeral).ConfigureAwait(false);
    }

    [Command("add"), RequiresBotModRole]
    [Description("Add a new entry to the schedule")]
    public static async ValueTask Add(
        SlashCommandContext ctx,
        [Description("Date and time, e.g. `2069-04-20 12:34 EDT` (default timezone = UTC)")]
        string start,
        [Description("Duration, e.g. `2d 1h 15m` or `2.1:15`")]
        string duration,
        [Description("Entry name, e.g. `Nintendo Direct <https://www.youtube.com/@NintendoAmerica>`")]
        string name,
        [Description("Optional event name, e.g. `E3` or `GDC` (without year)")]
        string? @event = null
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        var evt = new EventSchedule();
        if (!TimeParser.TryParse(start, out var newTime))
        {
            await ctx.RespondAsync($"Couldn't parse `{start}` as a start date and time", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }
        
        if (newTime < DateTime.UtcNow)
        {
            await ctx.RespondAsync("Specified time is in the past, are you sure it is correct?", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        evt.Start = newTime.Ticks;
        var newLength = await TryParseTimeSpanAsync(ctx, duration, false).ConfigureAwait(false);
        if (!newLength.HasValue)
        {
            await ctx.RespondAsync($"Couldn't parse `{duration}` as a duration", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        evt.End = (evt.Start.AsUtc() + newLength.Value).Ticks;
        if (string.IsNullOrEmpty(name))
        {
            await ctx.RespondAsync("Entry title cannot be empty",  ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        evt.Name = name;
        evt.EventName = string.IsNullOrWhiteSpace(@event) || @event == "-" ? null : @event;
        evt.Year = newTime.Year;
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        await wdb.EventSchedule.AddAsync(evt).ConfigureAwait(false);
        await wdb.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync(embed: FormatEvent(evt).WithTitle("Created new event schedule entry #" + evt.Id), ephemeral: ephemeral).ConfigureAwait(false);
    }

    [Command("remove"), RequiresBotModRole]
    [Description("Remove schedule entry")]
    public static async ValueTask RemoveGeneric(
        SlashCommandContext ctx,
        [Description("Event ID to remove"), SlashAutoCompleteProvider<EventIdAutoCompleteProvider>]
        int id
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var eventsToRemove = await wdb.EventSchedule.Where(evt => evt.Id == id).ToListAsync().ConfigureAwait(false);
        wdb.EventSchedule.RemoveRange(eventsToRemove);
        var removedCount = await wdb.SaveChangesAsync().ConfigureAwait(false);
        if (removedCount is 1)
            await ctx.RespondAsync($"{Config.Reactions.Success} Event successfully removed", ephemeral: ephemeral).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to remove event", ephemeral: ephemeral).ConfigureAwait(false);
    }

    /*
    [Command("clean"), TextAlias("cleanup", "Clear"), RequiresBotModRole]
    [Description("Removes past events")]
    public Task ClearGeneric(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
    {
        var currentYear = DateTime.UtcNow.Year;
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var itemsToRemove = await db.EventSchedule.Where(e =>
            year.HasValue
                ? e.Year == year
                : e.Year < currentYear
        ).ToListAsync().ConfigureAwait(false);
        db.EventSchedule.RemoveRange(itemsToRemove);
        var removedCount = await wdb.SaveChangesAsync().ConfigureAwait(false);
        await ctx.Channel.SendMessageAsync($"Removed {removedCount} event{(removedCount == 1 ? "" : "s")}").ConfigureAwait(false);
    }
    */

    [Command("update"), RequiresBotModRole]
    [Description("Update an event entry")]
    public static async ValueTask Update(
        SlashCommandContext ctx,
        [Description("Event ID"), SlashAutoCompleteProvider<EventIdAutoCompleteProvider>] int id,
        [Description("Date and time, e.g. `2069-04-20 12:34 EDT` (default timezone = UTC)")]
        string? start = null,
        [Description("Duration, e.g. `2d 1h 15m` or `2.1:15`")]
        string? duration = null,
        [Description("Entry name, e.g. `Nintendo Direct <https://www.youtube.com/@NintendoAmerica>`")]
        string? name = null,
        [Description("Optional event name, e.g. `E3` or `GDC` (without year)")]
        string? @event = null
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
        var evt = wdb.EventSchedule.FirstOrDefault(e => e.Id == id);
        if (evt is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} No event with id {id}", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (start is { Length: >0 })
        {
            if (!TimeParser.TryParse(start, out var newTime))
            {
                await ctx.RespondAsync($"Couldn't parse `{start}` as a start date and time", ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }

            evt.Start = newTime.Ticks;
        }
        if (duration is { Length: >0 })
        {
            var newLength = await TryParseTimeSpanAsync(ctx, duration, false).ConfigureAwait(false);
            if (!newLength.HasValue)
            {
                await ctx.RespondAsync($"Couldn't parse `{duration}` as a duration", ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }
            
            evt.End = (evt.Start.AsUtc() + newLength.Value).Ticks;
        }
        if (name is { Length: >0 })
            evt.Name = name;
        if (@event is { Length: >0 })
            evt.EventName = @event;

        await wdb.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync(embed: FormatEvent(evt).WithTitle("Updated event schedule entry #" + evt.Id), ephemeral: ephemeral).ConfigureAwait(false);
    }

    [Command("list")]
    [Description("List all scheduled entries")]
    public static async ValueTask List(
        SlashCommandContext ctx,
        [Description("Event name to list the schedule for, e.g. `E3` or `all`")]
        string? @event = null,
        [Description("Year of the event")]
        int? year = null)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() || ModProvider.IsMod(ctx.User.Id);
        var showAll = "all".Equals(@event, StringComparison.InvariantCultureIgnoreCase);
        var currentTicks = DateTime.UtcNow.Ticks;
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        IQueryable<EventSchedule> query = db.EventSchedule;
        if (year.HasValue)
            query = query.Where(e => e.Year == year);
        else if (!showAll)
            query = query.Where(e => e.End > currentTicks);
        if (!string.IsNullOrEmpty(@event) && !showAll)
        {
            @event = await FuzzyMatchEventName(db, @event).ConfigureAwait(false);
            query = query.Where(e => e.EventName == @event);
        }
        var events = await query
            .OrderBy(e => e.Start)
            .ToListAsync()
            .ConfigureAwait(false);
        if (events.Count is 0)
        {
            await ctx.RespondAsync("There are no events scheduled", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        var msg = new StringBuilder();
        var currentYear = -1;
        var currentEvent = Guid.NewGuid().ToString();
        foreach (var evt in events)
        {
            if (evt.Year != currentYear)
            {
                if (currentYear > 0)
                    msg.AppendLine().AppendLine($"__Year {evt.Year}__");
                currentEvent = Guid.NewGuid().ToString();
                currentYear = evt.Year;
            }

            var evtName = evt.EventName ?? "";
            if (currentEvent != evtName)
            {
                currentEvent = evtName;
                var printName = string.IsNullOrEmpty(currentEvent) ? "Various independent events" : $"**{currentEvent} {currentYear} schedule**";
                msg.AppendLine($"{printName} (UTC):");
            }
            msg.Append(StringUtils.InvisibleSpacer).Append('`');
            if (ModProvider.IsMod(ctx.User.Id))
                msg.Append($"[{evt.Id:0000}] ");
            msg.Append($"{evt.Start.AsUtc():u}");
            if (ctx.Channel.IsPrivate)
                msg.Append($@" - {evt.End.AsUtc():u}");
            msg.AppendLine($@" ({evt.End.AsUtc() - evt.Start.AsUtc():h\:mm})`: {evt.Name}");
        }
        //await ch.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
        var result = AutosplitResponseHelper.AutosplitMessage(msg.ToString(), blockStart: null, blockEnd: null);
        await ctx.RespondAsync(result[0], ephemeral: ephemeral).ConfigureAwait(false);
    }
    
    private static string? NameWithoutLink(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var lastPartIdx = name.LastIndexOf(' ');
        if (lastPartIdx < 0)
            return name;

        if (name.Length - lastPartIdx > 5
            && name.Substring(lastPartIdx + 1, 5).ToLowerInvariant() is string lnk
            && (lnk.StartsWith("<http") || lnk.StartsWith("http")))
            return name[..lastPartIdx];

        return name;
    }

    private static async ValueTask<string?> FuzzyMatchEventName(BotDb db, string? eventName)
    {
        var knownEventNames = await db.EventSchedule.Select(e => e.EventName).Distinct().ToListAsync().ConfigureAwait(false);
        var (score, name) = knownEventNames.Select(n => (score: eventName.GetFuzzyCoefficientCached(n), name: n)).OrderByDescending(t => t.score).FirstOrDefault();
        return score > 0.8 ? name : eventName;
    }

    private static async ValueTask<string?> FuzzyMatchEntryName(BotDb db, string? eventName)
    {
        var now = DateTime.UtcNow.Ticks;
        var knownNames = await db.EventSchedule.Where(e => e.End > now).Select(e => e.Name).ToListAsync().ConfigureAwait(false);
        var (score, name) = knownNames.Select(n => (score: eventName.GetFuzzyCoefficientCached(NameWithoutLink(n)), name: n)).OrderByDescending(t => t.score).FirstOrDefault();
        return score > 0.5 ? name : eventName;
    }

    private static async ValueTask<TimeSpan?> TryParseTimeSpanAsync(CommandContext ctx, string duration, bool react = true)
    {
        var d = Duration().Match(duration);
        if (!d.Success)
        {
            /*
            if (react)
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
            */
            return null;
        }

        _ = int.TryParse(d.Groups["days"].Value, out var days);
        _ = int.TryParse(d.Groups["hours"].Value, out var hours);
        _ = int.TryParse(d.Groups["mins"].Value, out var mins);
        if (days == 0 && hours == 0 && mins == 0)
        {
            /*
            if (react)
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
            */
            return null;
        }

        return new TimeSpan(days, hours, mins, 0);
    }

    private static string FormatCountdown(TimeSpan timeSpan)
    {
        var result = "";
        var days = (int)timeSpan.TotalDays;
        if (days > 0)
            timeSpan -= TimeSpan.FromDays(days);
        var hours = (int)timeSpan.TotalHours;
        if (hours > 0)
            timeSpan -= TimeSpan.FromHours(hours);
        var mins = (int)timeSpan.TotalMinutes;
        if (mins > 0)
            timeSpan -= TimeSpan.FromMinutes(mins);
        var secs = (int)timeSpan.TotalSeconds;
        if (days > 0)
            result += $"{days} day{(days == 1 ? "" : "s")} ";
        if (hours > 0 || days > 0)
            result += $"{hours} hour{(hours == 1 ? "" : "s")} ";
        if (mins > 0 || hours > 0 || days > 0)
            result += $"{mins} minute{(mins == 1 ? "" : "s")} ";
        result += $"{secs} second{(secs == 1 ? "" : "s")}";
        return result;
    }

    private static DiscordEmbedBuilder FormatEvent(EventSchedule evt, string? error = null, int highlight = -1)
    {
        var start = evt.Start.AsUtc();
        var field = 1;
        var result = new DiscordEmbedBuilder
        {
            Title = "Schedule entry preview",
            Color = string.IsNullOrEmpty(error) ? Config.Colors.Help : Config.Colors.Maintenance,
        };
        if (!string.IsNullOrEmpty(error))
            result.AddField("Entry error", error);
        var currentTime = DateTime.UtcNow;
        if (evt.Start > currentTime.Ticks)
            result.WithFooter($"Starts in {FormatCountdown(evt.Start.AsUtc() - currentTime)}");
        else if (evt.End > currentTime.Ticks)
            result.WithFooter($"Ends in {FormatCountdown(evt.End.AsUtc() - currentTime)}");
        var eventDuration = evt.End.AsUtc() - start;
        var durationFormat = eventDuration.TotalDays > 0 ? @"d\d\ h\h\ m\m" : @"h\h\ m\m";
        var startWarn = start < DateTime.UtcNow ? "⚠️ " : "";
        result
            .AddFieldEx(startWarn + "Start time", evt.Start == 0 ? "-" : start.ToString("u"), highlight == field++, true)
            .AddFieldEx("Duration", evt.Start == evt.End ? "-" : eventDuration.ToString(durationFormat), highlight == field++, true)
            .AddFieldEx("Event name", string.IsNullOrEmpty(evt.EventName) ? "-" : evt.EventName, highlight == field++, true)
            .AddFieldEx("Schedule entry title", string.IsNullOrEmpty(evt.Name) ? "-" : evt.Name, highlight == field++, true);
#if DEBUG
        result.WithFooter("Test bot instance");
#endif
        return result;
    }
}