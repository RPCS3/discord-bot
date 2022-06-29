using CompatBot.Database;

namespace CompatBot.Utils;

internal static class BotDbExtensions
{
    public static bool IsComplete(this EventSchedule evt)
    {
        return evt.Start > 0
               && evt.End > evt.Start
               && evt.Year > 0
               && !string.IsNullOrEmpty(evt.Name);
    }

    public static bool IsComplete(this Piracystring filter)
    {
        var result = !string.IsNullOrEmpty(filter.String)
                     && filter.String.Length >= Config.MinimumPiracyTriggerLength
                     && filter.Actions != 0;
        if (result && filter.Actions.HasFlag(FilterAction.ShowExplain))
            result = !string.IsNullOrEmpty(filter.ExplainTerm);
        return result;
    }
}