using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    internal class EventsBaseCommand: BaseCommandModuleCustom
    {
        private static readonly TimeSpan InteractTimeout = TimeSpan.FromMinutes(5);
        private static readonly Regex Duration = new(@"((?<days>\d+)(\.|d\s*))?((?<hours>\d+)(\:|h\s*))?((?<mins>\d+)m?)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        protected static async Task NearestEvent(CommandContext ctx, string? eventName = null)
        {
            var originalEventName = eventName = eventName?.Trim(40);
            var current = DateTime.UtcNow;
            var currentTicks = current.Ticks;
            await using var db = new BotDb();
            var currentEvents = await db.EventSchedule.OrderBy(e => e.End).Where(e => e.Start <= currentTicks && e.End >= currentTicks).ToListAsync().ConfigureAwait(false);
            var nextEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Start > currentTicks).ConfigureAwait(false);
            if (string.IsNullOrEmpty(eventName))
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
                await ctx.Channel.SendMessageAsync(nearestEventMsg.TrimEnd()).ConfigureAwait(false);
                return;
            }

            eventName = await FuzzyMatchEventName(db, eventName).ConfigureAwait(false);
            var promo = "";
            if (currentEvents.Count > 0)
                promo = $"\nMeanwhile check out this {(string.IsNullOrEmpty(currentEvents[0].EventName) ? "" : currentEvents[0].EventName + " " + currentEvents[0].Year + " ")}event in progress: {currentEvents[0].Name} (going for {FormatCountdown(current - currentEvents[0].Start.AsUtc())})";
            else if (nextEvent != null)
                promo = $"\nMeanwhile check out this upcoming {(string.IsNullOrEmpty(nextEvent.EventName) ? "" : nextEvent.EventName + " " + nextEvent.Year + " ")}event: {nextEvent.Name} (starts in {FormatCountdown(nextEvent.Start.AsUtc() - current)})";
            var firstNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year >= current.Year && e.EventName == eventName).ConfigureAwait(false);
            if (firstNamedEvent == null)
            {
                var scheduleEntry = await FuzzyMatchEntryName(db, originalEventName).ConfigureAwait(false);
                var events = await db.EventSchedule.OrderBy(e => e.Start).Where(e => e.End > current.Ticks && e.Name == scheduleEntry).ToListAsync().ConfigureAwait(false);
                if (events.Any())
                {
                    var eventListMsg = new StringBuilder();
                    foreach (var eventEntry in events)
                    {
                        if (eventEntry.Start < current.Ticks)
                            eventListMsg.AppendLine($"{eventEntry.Name} ends in {FormatCountdown(eventEntry.End.AsUtc() - current)}");
                        else
                            eventListMsg.AppendLine($"{eventEntry.Name} starts in {FormatCountdown(eventEntry.Start.AsUtc() - current)}");
                    }
                    await ctx.SendAutosplitMessageAsync(eventListMsg.ToString(), blockStart: "", blockEnd: "").ConfigureAwait(false);
                    return;
                }

                var noEventMsg = $"No information about the upcoming {eventName?.Sanitize(replaceBackTicks: true)} at the moment";
                if (eventName?.Length > 10)
                    noEventMsg = "No information about such event at the moment";
                else if (ctx.User.Id == 259997001880436737ul || ctx.User.Id == 377190919327318018ul)
                {
                    noEventMsg = $"Haha, very funny, {ctx.User.Mention}. So original. Never saw this joke before.";
                    promo = null;
                }
                if (!string.IsNullOrEmpty(promo))
                    noEventMsg += promo;
                await ctx.Channel.SendMessageAsync(noEventMsg).ConfigureAwait(false);
                return;
            }

            if (firstNamedEvent.Start >= currentTicks)
            {
                var upcomingNamedEventMsg = $"__{FormatCountdown(firstNamedEvent.Start.AsUtc() - current)} until {eventName} {firstNamedEvent.Year}!__";
                if (string.IsNullOrEmpty(promo) || nextEvent?.Id == firstNamedEvent.Id)
                    upcomingNamedEventMsg += $"\nFirst event: {firstNamedEvent.Name}";
                else
                    upcomingNamedEventMsg += promo;
                await ctx.Channel.SendMessageAsync(upcomingNamedEventMsg).ConfigureAwait(false);
                return;
            }

            var lastNamedEvent = await db.EventSchedule.OrderByDescending(e => e.End).FirstOrDefaultAsync(e => e.Year == current.Year && e.EventName == eventName).ConfigureAwait(false);
            if (lastNamedEvent is not null && lastNamedEvent.End <= currentTicks)
            {
                if (lastNamedEvent.End < current.AddMonths(-1).Ticks)
                {
                    firstNamedEvent = await db.EventSchedule.OrderBy(e => e.Start).FirstOrDefaultAsync(e => e.Year >= current.Year + 1 && e.EventName == eventName).ConfigureAwait(false);
                    if (firstNamedEvent == null)
                    {
                        var noEventMsg = $"No information about the upcoming {eventName?.Sanitize(replaceBackTicks: true)} at the moment";
                        if (eventName?.Length > 10)
                            noEventMsg = "No information about such event at the moment";
                        else if (ctx.User.Id == 259997001880436737ul || ctx.User.Id == 377190919327318018ul)
                        {
                            noEventMsg = $"Haha, very funny, {ctx.User.Mention}. So original. Never saw this joke before.";
                            promo = null;
                        }
                        if (!string.IsNullOrEmpty(promo))
                            noEventMsg += promo;
                        await ctx.Channel.SendMessageAsync(noEventMsg).ConfigureAwait(false);
                        return;
                    }
                        
                    var upcomingNamedEventMsg = $"__{FormatCountdown(firstNamedEvent.Start.AsUtc() - current)} until {eventName} {firstNamedEvent.Year}!__";
                    if (string.IsNullOrEmpty(promo) || nextEvent?.Id == firstNamedEvent.Id)
                        upcomingNamedEventMsg += $"\nFirst event: {firstNamedEvent.Name}";
                    else
                        upcomingNamedEventMsg += promo;
                    await ctx.Channel.SendMessageAsync(upcomingNamedEventMsg).ConfigureAwait(false);
                    return;
                }

                var e3EndedMsg = $"__{eventName} {current.Year} has concluded. See you next year! (maybe)__";
                if (!string.IsNullOrEmpty(promo))
                    e3EndedMsg += promo;
                await ctx.Channel.SendMessageAsync(e3EndedMsg).ConfigureAwait(false);
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

        protected static async Task Add(CommandContext ctx, string? eventName = null)
        {
            var evt = new EventSchedule();
            var (success, msg) = await EditEventPropertiesAsync(ctx, evt, eventName).ConfigureAwait(false);
            if (success)
            {
                await using var db = new BotDb();
                await db.EventSchedule.AddAsync(evt).ConfigureAwait(false);
                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
                if (LimitedToSpamChannel.IsSpamChannel(ctx.Channel))
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatEvent(evt).WithTitle("Created new event schedule entry #" + evt.Id)).ConfigureAwait(false);
                else
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Added a new schedule entry").ConfigureAwait(false);
            }
            else
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event creation aborted").ConfigureAwait(false);
        }

        protected static async Task Remove(CommandContext ctx, params int[] ids)
        {
            await using var db = new BotDb();
            var eventsToRemove = await db.EventSchedule.Where(e3e => ids.Contains(e3e.Id)).ToListAsync().ConfigureAwait(false);
            db.EventSchedule.RemoveRange(eventsToRemove);
            var removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            if (removedCount == ids.Length)
                await ctx.Channel.SendMessageAsync($"Event{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            else
                await ctx.Channel.SendMessageAsync($"Removed {removedCount} event{StringUtils.GetSuffix(removedCount)}, but was asked to remove {ids.Length}").ConfigureAwait(false);
        }

        protected static async Task Clear(CommandContext ctx, int? year = null)
        {
            var currentYear = DateTime.UtcNow.Year;
            await using var db = new BotDb();
            var itemsToRemove = await db.EventSchedule.Where(e =>
                year.HasValue
                    ? e.Year == year
                    : e.Year < currentYear
            ).ToListAsync().ConfigureAwait(false);
            db.EventSchedule.RemoveRange(itemsToRemove);
            var removedCount = await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.Channel.SendMessageAsync($"Removed {removedCount} event{(removedCount == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        protected static async Task Update(CommandContext ctx, int id, string? eventName = null)
        {
            await using var db = new BotDb();
            var evt = eventName == null
                ? db.EventSchedule.FirstOrDefault(e => e.Id == id)
                : db.EventSchedule.FirstOrDefault(e => e.Id == id && e.EventName == eventName);
            if (evt == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"No event with id {id}").ConfigureAwait(false);
                return;
            }

            var (success, msg) = await EditEventPropertiesAsync(ctx, evt, eventName).ConfigureAwait(false);
            if (success)
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
                if (LimitedToSpamChannel.IsSpamChannel(ctx.Channel))
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatEvent(evt).WithTitle("Updated event schedule entry #" + evt.Id)).ConfigureAwait(false);
                else
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Updated the schedule entry").ConfigureAwait(false);
            }
            else
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Event update aborted, changes weren't saved").ConfigureAwait(false);
        }

        protected static async Task List(CommandContext ctx, string? eventName = null, int? year = null)
        {
            var showAll = "all".Equals(eventName, StringComparison.InvariantCultureIgnoreCase);
            var currentTicks = DateTime.UtcNow.Ticks;
            await using var db = new BotDb();
            IQueryable<EventSchedule> query = db.EventSchedule;
            if (year.HasValue)
                query = query.Where(e => e.Year == year);
            else if (!showAll)
                query = query.Where(e => e.End > currentTicks);
            if (!string.IsNullOrEmpty(eventName) && !showAll)
            {
                eventName = await FuzzyMatchEventName(db, eventName).ConfigureAwait(false);
                query = query.Where(e => e.EventName == eventName);
            }
            List<EventSchedule> events = await query
                .OrderBy(e => e.Start)
                .ToListAsync()
                .ConfigureAwait(false);
            if (events.Count == 0)
            {
                await ctx.Channel.SendMessageAsync("There are no events to show").ConfigureAwait(false);
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
                if (ModProvider.IsMod(ctx.Message.Author.Id))
                    msg.Append($"[{evt.Id:0000}] ");
                msg.Append($"{evt.Start.AsUtc():u}");
                if (ctx.Channel.IsPrivate)
                    msg.Append($@" - {evt.End.AsUtc():u}");
                msg.AppendLine($@" ({evt.End.AsUtc() - evt.Start.AsUtc():h\:mm})`: {evt.Name}");
            }
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
        }

        private static async Task<(bool success, DiscordMessage? message)> EditEventPropertiesAsync(CommandContext ctx, EventSchedule evt, string? eventName = null)
        {
            var interact = ctx.Client.GetInteractivity();
            var abort = new DiscordButtonComponent(ButtonStyle.Danger, "event:edit:abort", "Cancel", emoji: new(DiscordEmoji.FromUnicode("✖")));
            var lastPage = new DiscordButtonComponent(ButtonStyle.Secondary, "event:edit:last", "To Last Field", emoji: new(DiscordEmoji.FromUnicode("⏭")));
            var firstPage = new DiscordButtonComponent(ButtonStyle.Secondary, "event:edit:first", "To First Field", emoji: new(DiscordEmoji.FromUnicode("⏮")));
            var previousPage = new DiscordButtonComponent(ButtonStyle.Secondary, "event:edit:previous", "Previous", emoji: new(DiscordEmoji.FromUnicode("◀")));
            var nextPage = new DiscordButtonComponent(ButtonStyle.Primary, "event:edit:next", "Next", emoji: new(DiscordEmoji.FromUnicode("▶")));
            var trash = new DiscordButtonComponent(ButtonStyle.Secondary, "event:edit:trash", "Clear", emoji: new(DiscordEmoji.FromUnicode("🗑")));
            var saveEdit = new DiscordButtonComponent(ButtonStyle.Success, "event:edit:save", "Save", emoji: new(DiscordEmoji.FromUnicode("💾")));

            var skipEventNameStep = !string.IsNullOrEmpty(eventName);
            DiscordMessage? msg = null;
            string? errorMsg = null;
            DiscordMessage? txt;
            ComponentInteractionCreateEventArgs? btn;

        step1:
            // step 1: get the new start date
            saveEdit.SetEnabled(evt.IsComplete());
            var messageBuilder = new DiscordMessageBuilder()
                .WithContent("Please specify a new **start date and time**")
                .WithEmbed(FormatEvent(evt, errorMsg, 1).WithDescription($"Example: `{DateTime.UtcNow:yyyy-MM-dd HH:mm} PST`\nBy default all times use UTC, only limited number of time zones supported"))
                .AddComponents(lastPage, nextPage)
                .AddComponents(saveEdit, abort);
            errorMsg = null;
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
            (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
            if (btn != null)
            {
                if (btn.Id == abort.CustomId)
                    return (false, msg);

                if (btn.Id == saveEdit.CustomId)
                    return (true, msg);

                if (btn.Id == lastPage.CustomId)
                    goto step4;
            }
            else if (txt != null)
            {
                if (!TimeParser.TryParse(txt.Content, out var newTime))
                {
                    errorMsg = $"Couldn't parse `{txt.Content}` as a start date and time";
                    goto step1;
                }
                if (newTime < DateTime.UtcNow && evt.End == default)
                    errorMsg = "Specified time is in the past, are you sure it is correct?";

                var duration = evt.End - evt.Start;
                evt.Start = newTime.Ticks;
                evt.End = evt.Start + duration;
                evt.Year = newTime.Year;
            }
            else
                return (false, msg);

        step2:
            // step 2: get the new duration
            saveEdit.SetEnabled(evt.IsComplete());
            messageBuilder = new DiscordMessageBuilder()
                .WithContent("Please specify a new **event duration**")
                .WithEmbed(FormatEvent(evt, errorMsg, 2).WithDescription("Example: `2d 1h 15m`, or `2.1:00`"))
                .AddComponents(previousPage, nextPage)
                .AddComponents(saveEdit, abort);
            errorMsg = null;
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
            (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
            if (btn != null)
            {
                if (btn.Id == abort.CustomId)
                    return (false, msg);

                if (btn.Id == saveEdit.CustomId)
                    return (true, msg);

                if (btn.Id == previousPage.CustomId)
                    goto step1;

                if (skipEventNameStep)
                    goto step4;
            }
            else if (txt != null)
            {
                var newLength = await TryParseTimeSpanAsync(ctx, txt.Content, false).ConfigureAwait(false);
                if (!newLength.HasValue)
                {
                    errorMsg = $"Couldn't parse `{txt.Content}` as a duration";
                    goto step2;
                }

                evt.End = (evt.Start.AsUtc() + newLength.Value).Ticks;
            }
            else
                return (false, msg);

        step3:
            // step 3: get the new event name
            saveEdit.SetEnabled(evt.IsComplete());
            trash.SetDisabled(string.IsNullOrEmpty(evt.EventName));
            messageBuilder = new DiscordMessageBuilder()
                .WithContent("Please specify a new **event name**")
                .WithEmbed(FormatEvent(evt, errorMsg, 3))
                .AddComponents(previousPage, nextPage, trash)
                .AddComponents(saveEdit, abort);
            errorMsg = null;
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
            (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
            if (btn != null)
            {
                if (btn.Id == abort.CustomId)
                    return (false, msg);

                if (btn.Id == saveEdit.CustomId)
                    return (true, msg);

                if (btn.Id == previousPage.CustomId)
                    goto step2;

                if (btn.Id == trash.CustomId)
                    evt.EventName = null;
            }
            else if (txt != null)
                evt.EventName = string.IsNullOrWhiteSpace(txt.Content) || txt.Content == "-" ? null : txt.Content;
            else
                return (false, msg);

        step4:
            // step 4: get the new schedule entry name
            saveEdit.SetEnabled(evt.IsComplete());
            messageBuilder = new DiscordMessageBuilder()
                .WithContent("Please specify a new **schedule entry title**")
                .WithEmbed(FormatEvent(evt, errorMsg, 4))
                .AddComponents(previousPage, firstPage)
                .AddComponents(saveEdit, abort);
            errorMsg = null;
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
            (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
            if (btn != null)
            {
                if (btn.Id == abort.CustomId)
                    return (false, msg);

                if (btn.Id == saveEdit.CustomId)
                    return (true, msg);

                if (btn.Id == firstPage.CustomId)
                    goto step1;

                if (btn.Id == previousPage.CustomId)
                {
                    if (skipEventNameStep)
                        goto step2;
                    goto step3;
                }
            }
            else if (txt != null)
            {
                if (string.IsNullOrEmpty(txt.Content))
                {
                    errorMsg = "Entry title cannot be empty";
                    goto step4;
                }

                evt.Name = txt.Content;
            }
            else
                return (false, msg);

        step5:
            // step 5: confirm
            if (errorMsg == null && !evt.IsComplete())
                errorMsg = "Some required properties are not defined";
            saveEdit.SetEnabled(evt.IsComplete());
            messageBuilder = new DiscordMessageBuilder()
                .WithContent("Does this look good? (y/n)")
                .WithEmbed(FormatEvent(evt, errorMsg))
                .AddComponents(previousPage, firstPage)
                .AddComponents(saveEdit, abort);
            errorMsg = null;
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
            (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
            if (btn != null)
            {
                if (btn.Id == abort.CustomId)
                    return (false, msg);

                if (btn.Id == saveEdit.CustomId)
                    return (true, msg);

                if (btn.Id == previousPage.CustomId)
                    goto step4;

                if (btn.Id == firstPage.CustomId)
                    goto step1;
            }
            else if (!string.IsNullOrEmpty(txt?.Content))
            {
                if (!evt.IsComplete())
                    goto step5;

                switch (txt.Content.ToLowerInvariant())
                {
                    case "yes":
                    case "y":
                    case "✅":
                    case "☑":
                    case "✔":
                    case "👌":
                    case "👍":
                        return (true, msg);
                    case "no":
                    case "n":
                    case "❎":
                    case "❌":
                    case "👎":
                        return (false, msg);
                    default:
                        errorMsg = "I don't know what you mean, so I'll just abort";
                        goto step5;
                }
            }
            else
            {
                return (false, msg);
            }

            return (false, msg);
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

        private static async Task<string?> FuzzyMatchEventName(BotDb db, string? eventName)
        {
            var knownEventNames = await db.EventSchedule.Select(e => e.EventName).Distinct().ToListAsync().ConfigureAwait(false);
            var (score, name) = knownEventNames.Select(n => (score: eventName.GetFuzzyCoefficientCached(n), name: n)).OrderByDescending(t => t.score).FirstOrDefault();
            return score > 0.8 ? name : eventName;
        }

        private static async Task<string?> FuzzyMatchEntryName(BotDb db, string? eventName)
        {
            var now = DateTime.UtcNow.Ticks;
            var knownNames = await db.EventSchedule.Where(e => e.End > now).Select(e => e.Name).ToListAsync().ConfigureAwait(false);
            var (score, name) = knownNames.Select(n => (score: eventName.GetFuzzyCoefficientCached(NameWithoutLink(n)), name: n)).OrderByDescending(t => t.score).FirstOrDefault();
            return score > 0.5 ? name : eventName;
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

            _ = int.TryParse(d.Groups["days"].Value, out var days);
            _ = int.TryParse(d.Groups["hours"].Value, out var hours);
            _ = int.TryParse(d.Groups["mins"].Value, out var mins);
            if (days == 0 && hours == 0 && mins == 0)
            {
                if (react)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Failed to parse `{duration}` as a time", true).ConfigureAwait(false);
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
            var startWarn = start < DateTime.UtcNow ? "⚠ " : "";
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
}
