using CompatBot.Database;

namespace CompatBot.Utils;

internal static class BotDbExtensions
{
    public static bool IsComplete(this EventSchedule evt)
        => evt is { Start: > 0, Year: > 0, Name.Length: >0 } 
           && evt.End > evt.Start;

    public static bool IsComplete(this Piracystring filter)
    {
        var result = filter.Actions != 0
                     && filter.String.Length >= Config.MinimumPiracyTriggerLength;
        if (result && filter.Actions.HasFlag(FilterAction.ShowExplain))
            result = !string.IsNullOrEmpty(filter.ExplainTerm);
        return result;
    }
}