using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    internal class EventsBaseCommand: BaseCommandModuleCustom
    {
        private static readonly Regex Duration = new Regex(@"((?<hours>\d+)[\:h ])?((?<mins>\d+)m?)?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        public async Task NearestEvent(CommandContext ctx, string eventName = null)
        {
            var current = DateTime.UtcNow;
            var currentTicks = current.Ticks;
            using (var db = new BotDb())
            {
                var currentEvent = await db.EventSchedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.Start <= currentTicks && e.End >= currentTicks).ConfigureAwait(false);
                var nextEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks).ConfigureAwait(false);
                if (string.IsNullOrEmpty(eventName))
                {
                    var nearestEventMsg = "";
                    if (currentEvent != null)
                        nearestEventMsg = $"Current event: {currentEvent.Name} (going for {FormatCountdown(current - currentEvent.Start.AsUtc())})\n";
                    if (nextEvent != null)
                        nearestEventMsg += $"Next event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
                    await ctx.RespondAsync(nearestEventMsg.TrimEnd()).ConfigureAwait(false);
                    return;
                }

                eventName = await FuzzyMatchEventName(db, eventName).ConfigureAwait(false);
                var promo = "";
                if (currentEvent != null)
                    promo = $"\nMeanwhile check out this {(string.IsNullOrEmpty(currentEvent.EventName) ? "" : currentEvent.EventName + " " + currentEvent.Year + " ")}event in progress: {currentEvent.Name} (going for {FormatCountdown(current - currentEvent.Start.AsUtc())})";
                else if (nextEvent != null)
                    promo = $"\nMeanwhile check out this upcoming {(string.IsNullOrEmpty(nextEvent.EventName) ? "" : nextEvent.EventName + " " + nextEvent.Year + " ")}event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
                var firstNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year >= current.Year && e.EventName == eventName).ConfigureAwait(false);
                if (firstNamedEvent == null)
                {
                    var noEventMsg = $"No information about the upcoming {eventName} at the moment";
                    if (!string.IsNullOrEmpty(promo))
                        noEventMsg += promo;
                    await ctx.RespondAsync(noEventMsg).ConfigureAwait(false);
                    return;
                }

                if (firstNamedEvent.Start >= currentTicks)
                {
                    var upcomingNamedEventMsg = $"__{FormatCountdown(firstNamedEvent.Start.AsUtc() - current)} until {eventName} {current.Year}!__";
                    if (string.IsNullOrEmpty(promo))
                        upcomingNamedEventMsg += $"\nFirst event: {firstNamedEvent.Name}";
                    else
                        upcomingNamedEventMsg += promo;
                    await ctx.RespondAsync(upcomingNamedEventMsg).ConfigureAwait(false);
                    return;
                }

                var lastNamedEvent = await db.EventSchedule.OrderByDescending(e => e.End).FirstOrDefaultAsync(e => e.Year == current.Year && e.EventName == eventName).ConfigureAwait(false);
                if (lastNamedEvent.End <= currentTicks)
                {
                    var e3EndedMsg = $"__{eventName} {current.Year} has ended. See you next year!__";
                    if (!string.IsNullOrEmpty(promo))
                        e3EndedMsg += promo;
                    await ctx.RespondAsync(e3EndedMsg).ConfigureAwait(false);
                    return;
                }

                var currentNamedEvent = await db.EventSchedule.OrderBy(e => e.End).FirstOrDefaultAsync(e => e.Start <= currentTicks && e.End >= currentTicks && e.EventName == eventName).ConfigureAwait(false);
                var nextNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks && e.EventName == eventName).ConfigureAwait(false);
                var msg = $"__{eventName} {current.Year} is already in progress!__\n";
                if (currentNamedEvent != null)
                    msg += $"Current event: {currentNamedEvent.Name} (going for {FormatCountdown(current - currentNamedEvent.Start.AsUtc())})\n";
                if (nextNamedEvent != null)
                    msg += $"Next event: {nextNamedEvent.Name} (starts in {FormatCountdown(nextNamedEvent.Start.AsUtc() - current)})";
                await ctx.SendAutosplitMessageAsync(msg.TrimEnd(), blockStart: "", blockEnd: "").ConfigureAwait(false);
            }
        }

        public async Task Add(CommandContext ctx, string eventName, string start, string duration, string entryName)
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
                await Add(ctx, eventName, startDateTime, endDateTime, entryName);
            }
        }

        public async Task Remove(CommandContext ctx, params int[] ids)
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

        public async Task Clear(CommandContext ctx, int? year = null)
        {
            var currentYear = DateTime.UtcNow.Year;
            int removedCount;
            using (var db = new BotDb())
            {
                var itemsToRemove = await db.EventSchedule.Where(e =>
                    year.HasValue
                        ? e.Year == year
                        : e.Year < currentYear
                ).ToListAsync().ConfigureAwait(false);
                db.EventSchedule.RemoveRange(itemsToRemove);
                removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.RespondAsync($"Removed {removedCount} event{(removedCount == 1 ? "" : "s")}").ConfigureAwait(false);
        }


        public async Task Update(CommandContext ctx, int id, string eventName = null)
        {
            using (var db = new BotDb())
            {
                var evt = eventName == null
                    ? db.EventSchedule.FirstOrDefault(e => e.Id == id)
                    : db.EventSchedule.FirstOrDefault(e => e.Id == id && e.EventName == eventName);
                if (evt == null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"No event with id {id}").ConfigureAwait(false);
                    return;
                }

                var interact = ctx.Client.GetInteractivity();
                var back = DiscordEmoji.FromUnicode("⏪");
                var skip = DiscordEmoji.FromUnicode("⏩");
                var trash = DiscordEmoji.FromUnicode("🗑");
                var yes = DiscordEmoji.FromUnicode("✅");
                var no = DiscordEmoji.FromUnicode("⛔");

                var skipEventNameStep = !string.IsNullOrEmpty(eventName);
                DiscordMessage msg = null;
                MessageContext txt;
                ReactionContext emoji;
step1:
                // step 1: get the new start date
                var embed = FormatEvent(evt, 1).WithDescription($"Example: `{DateTime.UtcNow:yyyy-MM-dd HH:mm} [PST]`\nBy default all times use UTC, only limited number of time zones supported");
                msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, $"Please specify a new **start date and time**", embed: embed).ConfigureAwait(false);
                (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, skip).ConfigureAwait(false);
                if (emoji != null)
                    ; // skip
                else if (txt != null)
                {
                    var newStartTime = FixTimeString(txt.Message.Content);
                    if (!DateTime.TryParse(newStartTime, out var newTime))
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                        await msg.UpdateOrCreateMessageAsync(ctx.Channel, $"Couldn't parse `{newStartTime}` as a start date and time, changes weren't saved").ConfigureAwait(false);
                        return;
                    }

                    var duration = evt.End - evt.Start;
                    evt.Start = Normalize(newTime).Ticks;
                    evt.End = evt.Start + duration;
                }
                else
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                    return;
                }
step2:
                // step 2: get the new duration
                embed = FormatEvent(evt, 2).WithDescription("Example: `1h15m`, or `1:00`");
                msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify a new **event duration**", embed: embed.Build()).ConfigureAwait(false);
                (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, back, skip).ConfigureAwait(false);
                if (emoji != null)
                {
                    if (emoji.Emoji == back)
                        goto step1;
                    else
                    {
                        if (skipEventNameStep)
                            goto step4;
                        else
                            goto step3;
                    }
                }
                else if (txt != null)
                {
                    var newLength = await TryParseTimeSpanAsync(ctx, txt.Message.Content).ConfigureAwait(false);
                    if (!newLength.HasValue)
                    {
                        await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                        await msg.UpdateOrCreateMessageAsync(ctx.Channel, $"Couldn't parse `{txt.Message.Content}` as a duration, changes weren't saved").ConfigureAwait(false);
                        return;
                    }

                    evt.End = (evt.Start.AsUtc() + newLength.Value).Ticks;
                }
                else
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                    return;
                }
step3:
                // step 3: get the new event name
                embed = FormatEvent(evt, 3);
                msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify a new **event name**", embed: embed.Build()).ConfigureAwait(false);
                var availableReactions = string.IsNullOrEmpty(evt.EventName) ? new[] {back, skip} : new[] {back, trash, skip};
                (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, availableReactions).ConfigureAwait(false);
                if (emoji != null)
                {
                    if (emoji.Emoji == trash)
                        evt.EventName = null;
                    else if (emoji.Emoji == back)
                        goto step2;
                }
                else if (txt != null)
                    evt.EventName = string.IsNullOrEmpty(txt.Message.Content) ? null : txt.Message.Content;
                else
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                    return;
                }
step4:
                // step 4: get the new schedule entry name
                embed = FormatEvent(evt, 4);
                msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify a new **schedule entry title**", embed: embed.Build()).ConfigureAwait(false);
                (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, back, skip).ConfigureAwait(false);
                if (emoji != null)
                {
                    if (emoji.Emoji == back)
                    {
                        if (skipEventNameStep)
                            goto step2;
                        else
                            goto step3;
                    }
                }
                else if (!string.IsNullOrEmpty(txt?.Message.Content))
                    evt.Name = txt.Message.Content;
                else
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                    return;
                }
step5:
                // step 5: confirm
                embed = FormatEvent(evt);
                msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Does this look good? (y/n)", embed: embed.Build()).ConfigureAwait(false);
                (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, back, yes, no).ConfigureAwait(false);
                if (emoji != null)
                {
                    if (emoji.Emoji == back)
                        goto step4;
                    else if (emoji.Emoji == no)
                    {
                        await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                        return;
                    }
                }
                else if (!string.IsNullOrEmpty(txt?.Message.Content))
                {
                    switch (txt.Message.Content.ToLowerInvariant())
                    {
                        case "yes":
                        case "y":
                        case "✅":
                        case "☑":
                        case "✔":
                        case "👌":
                        case "👍":
                            break;
                        case "no":
                        case "n":
                        case "❎":
                        case "❌":
                        case "👎":
                            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                            return;
                        default:
                            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "I don't know what you mean, so I'll just abort; changes weren't saved").ConfigureAwait(false);
                            return;
                    }
                }
                else
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
                    return;
                }

                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Updated the schedule entry").ConfigureAwait(false);
            }
        }
       

        public async Task List(CommandContext ctx, string eventName = null, int? year = null)
        {
            var currentTicks = DateTime.UtcNow.Ticks;
            List<EventSchedule> events;
            using (var db = new BotDb())
            {
                IQueryable<EventSchedule> query = db.EventSchedule;
                if (year.HasValue)
                    query = query.Where(e => e.Year == year);
                else if (!ctx.Channel.IsPrivate)
                    query = query.Where(e => e.End > currentTicks);
                if (!string.IsNullOrEmpty(eventName))
                {
                    eventName = await FuzzyMatchEventName(db, eventName).ConfigureAwait(false);
                    query = query.Where(e => e.EventName == eventName);
                }
                events = await query
                    .OrderBy(e => e.Start)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
            if (events.Count == 0)
            {
                await ctx.RespondAsync("There are no events to show").ConfigureAwait(false);
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

                var evtName = evt.EventName ?? "";
                if (currentEvent != evtName)
                {
                    currentEvent = evtName;
                    var printName = string.IsNullOrEmpty(currentEvent) ? "Various independent events" : $"**{currentEvent} {currentYear} schedule**";
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

        private async Task Add(CommandContext ctx, string eventName, DateTime start, DateTime end, string name)
        {
            start = Normalize(start);
            end = Normalize(end);
            var year = start.Year;

            /*
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

                        if (DateTime.UtcNow.Year < year)
                        {
                            await ctx.ReactWithAsync(Config.Reactions.Failure, "Aren't you a bit hasty?").ConfigureAwait(false);
                            return;
                        }
            */

            var startTicks = start.Ticks;
            var endTicks = end.Ticks;
            using (var db = new BotDb())
            {
                /*
                                var entries = await db.EventSchedule.Where(e => e.Year == year).OrderBy(e => e.Start).ToListAsync().ConfigureAwait(false);
                                var overlaps = entries.Where(e =>
                                        e.Start >= startTicks && e.Start < endTicks // existing event starts inside
                                        || e.End > startTicks && e.End <= endTicks // existing event ends inside
                                ).ToList();
                                if (overlaps.Any())
                                {
                                    var msg = new StringBuilder().AppendLine($"Specified event overlaps with the following event{(overlaps.Count == 1 ? "" : "s")}:");
                                    foreach (var evt in overlaps)
                                        msg.AppendLine($"`{evt.Start.AsUtc():u} - {evt.End.AsUtc():u}`: {evt.Name}");
                                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                                    await ctx.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
                                    return;
                                }
                */

                await db.EventSchedule.AddAsync(new EventSchedule
                {
                    Year = year,
                    EventName = eventName,
                    Start = startTicks,
                    End = endTicks,
                    Name = name,
                }).ConfigureAwait(false);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            await ctx.ReactWithAsync(Config.Reactions.Success, $"Added new {(string.IsNullOrEmpty(eventName) ? "" : eventName + " ")}event: `{name}`").ConfigureAwait(false);
        }

        private static async Task<string> FuzzyMatchEventName(BotDb db, string eventName)
        {
            var knownEventNames = await db.EventSchedule.Select(e => e.EventName).Distinct().ToListAsync().ConfigureAwait(false);
            var (score, name) = knownEventNames.Select(n => (score: eventName.GetFuzzyCoefficientCached(n), name: n)).OrderByDescending(t => t.score).FirstOrDefault();
            return score > 0.8 ? name : eventName;
        }

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

        private static async Task<TimeSpan?> TryParseTimeSpanAsync(CommandContext ctx, string duration, bool react = true)
        {
            var d = Duration.Match(duration);
            if (!d.Success)
            {
                if (react)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
                return null;
            }

            int.TryParse(d.Groups["hours"].Value, out var hours);
            int.TryParse(d.Groups["mins"].Value, out var mins);
            if (hours == 0 && mins == 0)
            {
                if (react)
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

        private static DiscordEmbedBuilder FormatEvent(EventSchedule evt, int highlight = -1)
        {
            var start = evt.Start.AsUtc();
            var field = 1;
            return new DiscordEmbedBuilder
                {
                    Title = "Schedule entry preview",
                    Color = Config.Colors.Help,
                }
                .AddFieldEx("Start time", evt.Start == 0 ? "-" : start.ToString("u"), highlight == field++, true)
                .AddFieldEx("Duration", evt.Start == evt.End ? "-" : (evt.End.AsUtc() - start).ToString(@"h\:mm"), highlight == field++, true)
                .AddFieldEx("Event name", string.IsNullOrEmpty(evt.EventName) ? "-" : evt.EventName, highlight == field++, true)
                .AddFieldEx("Schedule entry title", string.IsNullOrEmpty(evt.Name) ? "-" : evt.Name, highlight == field++, true);
        }
    }
}
