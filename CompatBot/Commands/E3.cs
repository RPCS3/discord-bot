using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands;

[Group("e3")]
[Description("Provides information about the E3 event")]
internal sealed class E3: EventsBaseCommand
{
    [GroupCommand]
    public Task E3Countdown(CommandContext ctx)
        => NearestEvent(ctx, "E3");

    [Command("add"), RequiresBotModRole]
    [Description("Adds new E3 event to the schedule")]
    public Task AddE3(CommandContext ctx)
        => Add(ctx, "E3");

    [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
    [Description("Removes event with the specified IDs")]
    public Task RemoveE3(CommandContext ctx, [Description("Event IDs to remove separated with space")] params int[] ids)
        => Remove(ctx, ids);
        

    [Command("clean"), Aliases("cleanup", "Clear"), RequiresBotModRole]
    [Description("Removes past events")]
    public Task ClearE3(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
        => Clear(ctx, year);

    [Command("edit"), Aliases("adjust", "change", "modify", "update"), RequiresBotModRole]
    [Description("Updates the event entry properties")]
    public Task AdjustE3(CommandContext ctx, [Description("Event ID")] int id)
        => Update(ctx, id, "E3");

    [Command("schedule"), Aliases("show", "list")]
    [Description("Outputs current schedule")]
    public Task ListE3(CommandContext ctx, [Description("Optional year to list")] int? year = null)
        => List(ctx, "E3", year);

    [Command("countdown")]
    [Description("Provides countdown for the nearest known E3 event")]
    public Task Countdown(CommandContext ctx)
        => E3Countdown(ctx);
}