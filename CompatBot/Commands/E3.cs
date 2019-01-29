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
                var firstEvent = await db.E3Schedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year == current.Year).ConfigureAwait(false);
                if (firstEvent == null)
                {
                    await ctx.RespondAsync("No information about the upcoming E3 at the moment").ConfigureAwait(false);
                    return;
                }
                if (firstEvent.Start >= currentTicks)
                {
                    await ctx.RespondAsync(
                        $"{FormatCountdown(firstEvent.Start.AsUtc() - current)} until E3 {current.Year}!\n" +
                        $"First event: {firstEvent.Name}"
                    ).ConfigureAwait(false);
                    return;
                }

                var lastEvent = await db.E3Schedule.OrderByDescending(e => e.End).FirstOrDefaultAsync(e => e.Year == current.Year).ConfigureAwait(false);
                if (lastEvent.End <= currentTicks)
                {
                    await ctx.RespondAsync($"E3 {current.Year} has ended. See you next year!").ConfigureAwait(false);
                    return;
                }

                var currentEvent = await db.E3Schedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.End >= currentTicks).ConfigureAwait(false);
                var nextEvent = await db.E3Schedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start >= currentTicks).ConfigureAwait(false);
                var msg = $"E3 {current.Year} is already in progress!\n";
                if (currentEvent != null)
                    msg += $"Current event: {currentEvent.Name} (going for {FormatCountdown(current - currentEvent.Start.AsUtc())})\n";
                if (nextEvent != null)
                    msg += $"Next event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
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
            start = start.ToUpperInvariant()
                .Replace("PST", "-08:00")
                .Replace("EST", "-05:00")
                .Replace("BST", "-03:00")
                .Replace("AEST", "+10:00");
            if (!DateTime.TryParse(start, out var startDateTime))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{start}` as a date", true).ConfigureAwait(false);
                return;
            }

            startDateTime = Normalize(startDateTime);
            var d = Duration.Match(duration);
            if (!d.Success)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
                return;
            }

            int.TryParse(d.Groups["hours"].Value, out var hours);
            int.TryParse(d.Groups["mins"].Value, out var mins);
            if (hours == 0 && mins == 0)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
                return;
            }
            
            var timeDiff = new TimeSpan(0, hours, mins, 0);
            var endDateTime = startDateTime + timeDiff;
            await Add(ctx, startDateTime, endDateTime, name);
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
                var entries = await db.E3Schedule.Where(e => e.Year == year).OrderBy(e => e.Start).ToListAsync().ConfigureAwait(false);
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

                await db.E3Schedule.AddAsync(new E3Schedule
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
                var eventsToRemove = await db.E3Schedule.Where(e3e => ids.Contains(e3e.Id)).ToListAsync().ConfigureAwait(false);
                db.E3Schedule.RemoveRange(eventsToRemove);
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
                var itemsToRemove = await db.E3Schedule.Where(e3e =>
                    year.HasValue
                        ? e3e.Year == year
                        : e3e.Year < currentYear
                ).ToListAsync().ConfigureAwait(false);
                db.E3Schedule.RemoveRange(itemsToRemove);
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.RespondAsync($"Removed {removedCount} event{(removedCount == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        [Command("schedule"), Aliases("show", "list")]
        [Description("Outputs current schedule")]
        public async Task List(CommandContext ctx, [Description("Optional year to list")] int? year = null)
        {
            List<E3Schedule> events;
            using (var db = new BotDb())
            {
                IQueryable<E3Schedule> query = db.E3Schedule;
                if (year.HasValue)
                    query = query.Where(e3e => e3e.Year == year);
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
            foreach (var evt in events)
            {
                if (evt.Year != currentYear)
                {
                    if (currentYear > 0)
                        msg.AppendLine();
                    currentYear = evt.Year;
                    msg.AppendLine($"**{(evt.Year == DateTime.UtcNow.Year ? "Current E3" : "E3 " + evt.Year)} schedule**:");
                }

                msg.Append("`");
                if (ctx.Channel.IsPrivate && ModProvider.IsMod(ctx.Message.Author.Id))
                    msg.Append($"[{evt.Id:0000}] ");
                msg.AppendLine($"{evt.Start.AsUtc():u} - {evt.End.AsUtc():u}`: {evt.Name}");
            }
            await ctx.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
        }

        [Command("countdown")]
        [Description("Provides countdown for the nearest known E3 event")]
        public Task Countdown(CommandContext ctx) => E3Countdown(ctx);

        private static DateTime Normalize(DateTime date)
        {
            if (date.Kind == DateTimeKind.Utc)
                return date;
            if (date.Kind == DateTimeKind.Local)
                return date.ToUniversalTime();
            return date.AsUtc();
        }

        private string FormatCountdown(TimeSpan timeSpan)
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
