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
    [Group("events"), Aliases("event", "e")]
    [Description("Provides information about the various events in the game industry")]
    internal sealed class Events: EventsBaseCommand
    {
        [GroupCommand]
        public Task NearestGenericEvent(CommandContext ctx, [Description("Optional event name"), RemainingText] string eventName = null)
            => NearestEvent(ctx, eventName);

        [Command("add"), RequiresBotModRole]
        [Description("Adds a new entry to the schedule")]
        public Task AddGeneric(CommandContext ctx,
            [Description("Event start time in `yyyy-mm-dd hh:mm[:ss][z]` format (24-hour, z for UTC), inclusive")] string start,
            [Description("Event duration (e.g. 1h30m or 1:00)")] string duration,
            [Description("Event entry name (e.g. Microsoft or Nintendo Direct)")] string entryName)
            => Add(ctx, null, start, duration, entryName);

        [Command("add"), RequiresBotModRole]
        [Description("Adds a new entry to the schedule")]
        public Task AddGeneric(CommandContext ctx,
            [Description("Event name (e.g. E3 or CES)")] string eventName,
            [Description("Event start time in `yyyy-mm-dd hh:mm[:ss][z]` format (24-hour, z for UTC), inclusive")] string start,
            [Description("Event duration (e.g. 1h30m or 1:00)")] string duration,
            [Description("Event entry name (e.g. Microsoft or Nintendo Direct)")] string entryName)
            => Add(ctx, eventName, start, duration, entryName);

        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes schedule entries with the specified IDs")]
        public Task RemoveGeneric(CommandContext ctx, [Description("Event IDs to remove separated with space")] params int[] ids)
            => Remove(ctx, ids);

        [Command("Clean"), Aliases("cleanup", "Clear"), RequiresBotModRole]
        [Description("Removes past events")]
        public Task ClearGeneric(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
             => Clear(ctx, year);

        [Command("rename"), RequiresBotModRole]
        [Description("Renames schedule entry with the specified ID")]
        public Task RenameGeneric(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newName)
            => Rename(ctx, id, newName);

        [Command("assign"), Aliases("tag"), RequiresBotModRole]
        [Description("Assignes specified schedule entry to specific event")]
        public Task AssignGeneric(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newName)
            => Assign(ctx, id, newName);

        [Command("shift"), RequiresBotModRole]
        [Description("Moves schedule entry to a new time")]
        public Task ShiftGeneric(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newStartTime)
            => Shift(ctx, id, newStartTime);

        [Command("adjust"), Aliases("shrink", "stretch"), RequiresBotModRole]
        [Description("Adjusts schedule entry duration")]
        public Task AdjustGeneric(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newDuration)
            => Adjust(ctx, id, newDuration);

        [Command("schedule"), Aliases("show", "list")]
        [Description("Outputs current schedule")]
        public Task ListGeneric(CommandContext ctx,
            [Description("Optional event name to list schedule for")] string eventName = null,
            [Description("Optional year to list")] int? year = null)
            => List(ctx, eventName, year);

        [Command("countdown")]
        [Description("Provides countdown for the nearest known event")]
        public Task Countdown(CommandContext ctx, string eventName = null)
            => NearestEvent(ctx, eventName);
    }
}
