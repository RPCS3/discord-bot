namespace CompatBot.Commands;

[Command("event"), TextAlias("events", "e")]
[Description("Provides information about the various events in the game industry")]
internal sealed class Events: EventsBaseCommand
{
    [Command("show"), DefaultGroupCommand]
    public Task Show(CommandContext ctx, [Description("Optional event name"), RemainingText] string? eventName = null)
        => NearestEvent(ctx, eventName);

    [Command("add"), RequiresBotModRole]
    [Description("Adds a new entry to the schedule")]
    public Task AddGeneric(CommandContext ctx)
        => Add(ctx);

    [Command("remove"), TextAlias("delete", "del"), RequiresBotModRole]
    [Description("Removes schedule entries with the specified IDs")]
    public Task RemoveGeneric(CommandContext ctx, [Description("Event IDs to remove separated with space")] params int[] ids)
        => Remove(ctx, ids);

    [Command("clean"), TextAlias("cleanup", "Clear"), RequiresBotModRole]
    [Description("Removes past events")]
    public Task ClearGeneric(CommandContext ctx, [Description("Optional year to remove, by default everything before current year")] int? year = null)
        => Clear(ctx, year);

    [Command("edit"), TextAlias("adjust", "change", "modify", "update"), RequiresBotModRole]
    [Description("Updates the event entry properties")]
    public Task AdjustGeneric(CommandContext ctx, [Description("Event ID")] int id)
        => Update(ctx, id);

    [Command("schedule"), TextAlias("show", "list")]
    [Description("Outputs current schedule")]
    public Task ListGeneric(CommandContext ctx)
        => List(ctx);

    [Command("schedule")]
    public Task ListGeneric(CommandContext ctx,
        [Description("Optional year to list")] int year)
        => List(ctx, null, year);

    [Command("schedule")]
    public Task ListGeneric(CommandContext ctx,
        [Description("Optional event name to list schedule for")] string eventName)
        => List(ctx, eventName);

    [Command("schedule")]
    public Task ListGeneric(CommandContext ctx,
        [Description("Optional event name to list schedule for")] string eventName,
        [Description("Optional year to list")] int year)
        => List(ctx, eventName, year);

    [Command("countdown")]
    [Description("Provides countdown for the nearest known event")]
    public Task Countdown(CommandContext ctx, string? eventName = null)
        => NearestEvent(ctx, eventName);
}