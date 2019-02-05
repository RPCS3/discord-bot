using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    [Group("e3")]
    [Description("Provides information about the E3 event")]
    internal sealed class E3: EventsBaseCommand
    {
        [GroupCommand]
        public Task E3Countdown(CommandContext ctx)
            => NearestEvent(ctx, "E3");

        [Command("add"), RequiresBotModRole]
        [Description("Adds new E3 event to the schedule")]
        public Task AddE3(CommandContext ctx,
            [Description("Event start time in `yyyy-mm-dd hh:mm[:ss][z]` format (24-hour, z for UTC), inclusive")] string start,
            [Description("Event duration (e.g. 1h30m or 1:00)")] string duration,
            [Description("Event entry name"), RemainingText] string name)
            => Add(ctx, "E3", start, duration, name);

        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes event with the specified IDs")]
        public Task RemoveE3(CommandContext ctx, [Description("Event IDs to remove separated with space")] params int[] ids)
            => Remove(ctx, ids);
        

        [Command("Clean"), Aliases("cleanup", "Clear"), RequiresBotModRole]
        [Description("Removes past events")]
        public Task ClearE3(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
            => Clear(ctx, year);

        [Command("rename"), RequiresBotModRole]
        [Description("Renames event with the specified ID")]
        public Task RenameE3(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newName)
            => Rename(ctx, id, newName);

        [Command("shift"), RequiresBotModRole]
        [Description("Moves event to a new time")]
        public Task ShiftE3(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newStartTime)
            => Shift(ctx, id, newStartTime);

        [Command("adjust"), Aliases("shrink", "stretch"), RequiresBotModRole]
        [Description("Adjusts event length")]
        public Task AdjustE3(CommandContext ctx, [Description("Event ID")] int id, [RemainingText] string newDuration)
            => Adjust(ctx, id, newDuration);

        [Command("schedule"), Aliases("show", "list")]
        [Description("Outputs current schedule")]
        public Task ListE3(CommandContext ctx, [Description("Optional year to list")] int? year = null)
            => List(ctx, "E3", year);

        [Command("countdown")]
        [Description("Provides countdown for the nearest known E3 event")]
        public Task Countdown(CommandContext ctx)
            => E3Countdown(ctx);
    }
}
