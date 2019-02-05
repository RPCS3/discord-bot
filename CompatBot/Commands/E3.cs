using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("e3")]
    [Description("Provides information about the E3 event")]
    internal sealed class E3: BaseCommandModuleCustom
    {
        private static readonly Regex Duration = new Regex(@"(?<hours>\d+)[\:h ]?((?<mins>\d+)m?)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        [GroupCommand]
        public async Task E3Countdown(CommandContext ctx)
        {
            var current = DateTime.UtcNow;
            var currentTicks = current.Ticks;
            using (var db = new BotDb())
            {
                var currentEvent = await db.EventSchedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.Start <= currentTicks && e.End >= currentTicks).ConfigureAwait(false);
                var nextEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks).ConfigureAwait(false);
                var promo = "";
                if (currentEvent != null)
                    promo = $"\nMeanwhile check out this {(string.IsNullOrEmpty(currentEvent.EventName) ? "" : currentEvent.EventName + " " + currentEvent.Year + " ")}event in progress: {currentEvent.Name} (going for {FormatCountdown(current - currentEvent.Start.AsUtc())})";
                else if (nextEvent != null)
                    promo = $"\nMeanwhile check out this upcoming {(string.IsNullOrEmpty(nextEvent.EventName) ? "" : nextEvent.EventName + " " + nextEvent.Year + " ")}event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";

                var firstE3Event = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year == current.Year && e.EventName == "E3").ConfigureAwait(false);
                if (firstE3Event == null)
                {
                    var noEventMsg = "No information about the upcoming E3 at the moment";
                    if (!string.IsNullOrEmpty(promo))
                        noEventMsg += promo;
                    await ctx.RespondAsync(noEventMsg).ConfigureAwait(false);
                    return;
                }

                if (firstE3Event.Start >= currentTicks)
                {
                    var upcomingE3Msg = $"__{FormatCountdown(firstE3Event.Start.AsUtc() - current)} until E3 {current.Year}!__";
                    if (string.IsNullOrEmpty(promo))
                        upcomingE3Msg += $"\nFirst event: {firstE3Event.Name}";
                    else
                        upcomingE3Msg += promo;
                    await ctx.RespondAsync(upcomingE3Msg).ConfigureAwait(false);
                    return;
                }

                var lastE3Event = await db.EventSchedule.OrderByDescending(e => e.End).FirstOrDefaultAsync(e => e.Year == current.Year && e.EventName == "E3").ConfigureAwait(false);
                if (lastE3Event.End <= currentTicks)
                {
                    var e3EndedMsg = $"E3 {current.Year} has ended. See you next year!";
                    if (!string.IsNullOrEmpty(promo))
                        e3EndedMsg += promo;
                    await ctx.RespondAsync(e3EndedMsg).ConfigureAwait(false);
                    return;
                }

                var currentE3Event = await db.EventSchedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.Start <= currentTicks && e.End >= currentTicks && e.EventName == "E3").ConfigureAwait(false);
                var nextE3Event = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks && e.EventName == "E3").ConfigureAwait(false);
                var msg = $"E3 {current.Year} is already in progress!\n";
                if (currentE3Event != null)
                    msg += $"Current event: {currentE3Event.Name} (going for {FormatCountdown(current - currentE3Event.Start.AsUtc())})\n";
                if (nextE3Event != null)
                    msg += $"Next event: {nextE3Event.Name} (starts in {FormatCountdown(nextE3Event.Start.AsUtc() - current)})";
                await ctx.SendAutosplitMessageAsync(msg.TrimEnd(), blockStart: "", blockEnd: "").ConfigureAwait(false);
            }
        }

        [Command("add"), RequiresBotModRole]
        [Description("Adds new E3 event to the schedule")]
        public async Task Add(CommandContext ctx,
            [Description("Event start time in `yyyy-mm-dd hh:mm[:ss][z]` format (24-hour, z for UTC), inclusive")] string start,
            [Description("Event duration (e.g. 1h30m or 1:00)")] string duration,
            [Description("Event name"), RemainingText] string name)
        {
            start = FixTimeString(start);
            if (!DateTime.TryParse(start, out var startDateTime))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{start}` as a date", true).ConfigureAwait(false);
                return;
            }

            startDateTime = Normalize(startDateTime);
            var timeDiff = await TryParseTimeSpanAsync(ctx, duration).ConfigureAwait(false);
            if (timeDiff.HasValue)
            {
                var endDateTime = startDateTime + timeDiff.Value;
                await Add(ctx, startDateTime, endDateTime, name);
            }
        }

        public async Task Add(CommandContext ctx, DateTime start, DateTime end, string name)
        {
            start = Normalize(start);
            end = Normalize(end);
            if (end < start)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Start date must be before End date", true).ConfigureAwait(false);
                return;
            }

            if (start.Year != end.Year)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Start and End dates must be for the same year", true).ConfigureAwait(false);
                return;
            }

            var year = start.Year;
            if (DateTime.UtcNow.Year < year)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Aren't you a bit hasty?").ConfigureAwait(false);
                return;
            }

            var startTicks = start.Ticks;
            var endTicks = end.Ticks;
            using (var db = new BotDb())
            {
                var entries = await db.EventSchedule.Where(e => e.Year == year).OrderBy(e => e.Start).ToListAsync().ConfigureAwait(false);
                var overlaps = entries.Where(e =>
                        e.Start >= startTicks && e.Start < endTicks // existing event starts inside
                        || e.End > startTicks && e.End <= endTicks // existing event ends inside
                ).ToList();
                if (overlaps.Any())
                {
                    var msg = new StringBuilder()
                        .AppendLine($"Specified event overlaps with the following event{(overlaps.Count == 1 ? "" : "s")}:");
                    foreach (var evt in overlaps)
                        msg.AppendLine($"`{evt.Start.AsUtc():u} - {evt.End.AsUtc():u}`: {evt.Name}");
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await ctx.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
                    return;
                }

                await db.EventSchedule.AddAsync(new EventSchedule
                {
                    Year = year,
                    Start = startTicks,
                    End = endTicks,
                    Name = name,
                }).ConfigureAwait(false);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Added new event: `{name}`").ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes event with the specified IDs")]
        public async Task Remove(CommandContext ctx, [Description("Event IDs to remove separated with space")] params int[] ids)
        {
            int removedCount;
            using (var db = new BotDb())
            {
                var eventsToRemove = await db.EventSchedule.Where(e3e => ids.Contains(e3e.Id)).ToListAsync().ConfigureAwait(false);
                db.EventSchedule.RemoveRange(eventsToRemove);
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            if (removedCount == ids.Length)
                await ctx.RespondAsync($"Event{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            else
                await ctx.RespondAsync($"Removed {removedCount} event{StringUtils.GetSuffix(removedCount)}, but was asked to remove {ids.Length}").ConfigureAwait(false);
        }

        [Command("Clean"), Aliases("cleanup", "Clear"), RequiresBotModRole]
        [Description("Removes past events")]
        public async Task Clear(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
        {
            var currentYear = DateTime.UtcNow.Year;
            int removedCount;
            using (var db = new BotDb())
            {
                var itemsToRemove = await db.EventSchedule.Where(e3e =>
                    year.HasValue
                        ? e3e.Year == year
                        : e3e.Year < currentYear
                ).ToListAsync().ConfigureAwait(false);
                db.EventSchedule.RemoveRange(itemsToRemove);
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.RespondAsync($"Removed {removedCount} event{(removedCount == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        [Command("rename"), RequiresBotModRole]
        [Description("Renames event with the specified ID")]
        public async Task Rename(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newName)
        {
            using (var db = new BotDb())
            {
                var evt = await db.EventSchedule.FirstOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
                if (evt == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"No event with id {id}").ConfigureAwait(false);
                else
                {
                    var oldName = evt.Name;
                    evt.Name = newName;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed `{oldName}` to `{newName}`").ConfigureAwait(false);
                }
            }
        }

        [Command("shift"), RequiresBotModRole]
        [Description("Moves event to a new time")]
        public async Task Shift(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newStartTime)
        {
            newStartTime = FixTimeString(newStartTime);
            if (!DateTime.TryParse(newStartTime, out var newTime))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Couldn't parse `{newStartTime}` as a date").ConfigureAwait(false);
                return;
            }

            using (var db = new BotDb())
            {
                var evt = await db.EventSchedule.FirstOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
                if (evt == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"No event with id {id}").ConfigureAwait(false);
                else
                {
                    var oldTime = evt.Start;
                    evt.Start = Normalize(newTime).Ticks;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Shifted {evt.Name} from {oldTime.AsUtc():u} to {newTime:u}").ConfigureAwait(false);
                }
            }
        }

        [Command("adjust"), Aliases("shrink", "stretch"), RequiresBotModRole]
        [Description("Adjusts event length")]
        public async Task Adjust(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newDuration)
        {
            var newLength = await TryParseTimeSpanAsync(ctx, newDuration).ConfigureAwait(false);
            if (!newLength.HasValue)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Couldn't parse `{newDuration}` as a time duration").ConfigureAwait(false);
                return;
            }

            using (var db = new BotDb())
            {
                var evt = await db.EventSchedule.FirstOrDefaultAsync(e => e.Id == id).ConfigureAwait(false);
                if (evt == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"No event with id {id}").ConfigureAwait(false);
                else
                {
                    var oldDuration = evt.End.AsUtc() - evt.Start.AsUtc();
                    evt.End = (evt.Start.AsUtc() + newLength.Value).Ticks;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $@"Adjusted {evt.Name} duration from {oldDuration:h\:mm} to {newLength:h\:mm}").ConfigureAwait(false);
                }
            }
        }

        [Command("schedule"), Aliases("show", "list")]
        [Description("Outputs current schedule")]
        public async Task List(CommandContext ctx, [Description("Optional year to list")] int? year = null)
        {
            List<EventSchedule> events;
            using (var db = new BotDb())
            {
                IQueryable<EventSchedule> query = db.EventSchedule;
                if (year.HasValue)
                    query = query.Where(e => e.Year == year);
                events = await query
                    .OrderBy(e => e.Start)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
            if (events.Count == 0)
            {
                await ctx.RespondAsync("There are no events recorded").ConfigureAwait(false);
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
                        msg.AppendLine();
                    currentEvent = Guid.NewGuid().ToString();
                    currentYear = evt.Year;
                }
                if (currentEvent != evt.EventName)
                {
                    currentEvent = evt.EventName;
                    var printName = string.IsNullOrEmpty(currentEvent) ? "Generic event schedule" : $"**{currentEvent} {currentYear} schedule**";
                    msg.AppendLine($"{printName} (UTC):");
                }

                msg.Append("`");
                if (ctx.Channel.IsPrivate && ModProvider.IsMod(ctx.Message.Author.Id))
                    msg.Append($"[{evt.Id:0000}] ");
                msg.Append($"{evt.Start.AsUtc():u}");
                if (ctx.Channel.IsPrivate)
                    msg.Append($@" - {evt.End.AsUtc():u}");
                msg.AppendLine($@" ({evt.End.AsUtc() - evt.Start.AsUtc():h\:mm})`: {evt.Name}");
            }
            await ctx.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
        }

        [Command("countdown")]
        [Description("Provides countdown for the nearest known E3 event")]
        public Task Countdown(CommandContext ctx) => E3Countdown(ctx);

        private static string FixTimeString(string dateTime)
        {
            return dateTime.ToUpperInvariant()
                .Replace("PST", "-08:00")
                .Replace("EST", "-05:00")
                .Replace("BST", "-03:00")
                .Replace("AEST", "+10:00");
        }

        private static DateTime Normalize(DateTime date)
        {
            if (date.Kind == DateTimeKind.Utc)
                return date;
            if (date.Kind == DateTimeKind.Local)
                return date.ToUniversalTime();
            return date.AsUtc();
        }

        private static async Task<TimeSpan?> TryParseTimeSpanAsync(CommandContext ctx, string duration)
        {
            var d = Duration.Match(duration);
            if (!d.Success)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
                return null;
            }

            int.TryParse(d.Groups["hours"].Value, out var hours);
            int.TryParse(d.Groups["mins"].Value, out var mins);
            if (hours == 0 && mins == 0)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
                return null;
            }

            return new TimeSpan(0, hours, mins, 0);
        }

        private static string FormatCountdown(TimeSpan timeSpan)
        {
            var result = "";
            var days = (int)timeSpan.TotalDays;
            if (days > 0)
                timeSpan -= TimeSpan.FromDays(days);
            var hours = (int) timeSpan.TotalHours;
            if (hours > 0)
                timeSpan -= TimeSpan.FromHours(hours);
            var mins = (int) timeSpan.TotalMinutes;
            if (mins > 0)
                timeSpan -= TimeSpan.FromMinutes(mins);
            var secs = (int) timeSpan.TotalSeconds;
            if (days > 0)
                result += $"{days} day{(days == 1 ? "" : "s")} ";
            if (hours > 0 || days > 0)
                result += $"{hours} hour{(hours == 1 ? "" : "s")} ";
            if (mins > 0 || hours > 0 || days > 0)
                result += $"{mins} minute{(mins == 1 ? "" : "s")} ";
            result += $"{secs} second{(secs == 1 ? "" : "s")}";
            return result;
        }
    }
}
