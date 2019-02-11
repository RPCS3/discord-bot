using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    [Group("event"), Aliases("events", "e")]
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

        [Command("clean"), Aliases("cleanup", "Clear"), RequiresBotModRole]
        [Description("Removes past events")]
        public Task ClearGeneric(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
             => Clear(ctx, year);

        [Command("update"), Aliases("adjust", "change", "modify"), RequiresBotModRole]
        [Description("Updates the event entry properties")]
        public Task AdjustGeneric(CommandContext ctx, [Description("Event ID")] int id)
            => Update(ctx, id);

        [Command("schedule"), Aliases("show", "list")]
        [Description("Outputs current schedule")]
        public Task ListGeneric(CommandContext ctx)
            => List(ctx, null, null);

        [Command("schedule")]
        public Task ListGeneric(CommandContext ctx,
            [Description("Optional year to list")] int year)
            => List(ctx, null, year);

        [Command("schedule")]
        public Task ListGeneric(CommandContext ctx,
            [Description("Optional event name to list schedule for")] string eventName)
            => List(ctx, eventName, null);

        [Command("schedule")]
        public Task ListGeneric(CommandContext ctx,
            [Description("Optional event name to list schedule for")] string eventName,
            [Description("Optional year to list")] int year)
            => List(ctx, eventName, year);

        [Command("countdown")]
        [Description("Provides countdown for the nearest known event")]
        public Task Countdown(CommandContext ctx, string eventName = null)
            => NearestEvent(ctx, eventName);
    }
}
