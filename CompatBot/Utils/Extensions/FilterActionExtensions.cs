using CompatBot.Database;

namespace CompatBot.Utils.Extensions;

internal static class FilterActionExtensions
{
    internal static readonly FilterAction[] ActionFlagValues = Enum.GetValues<FilterAction>();
    private static readonly Dictionary<FilterAction, char> ActionFlagToChar = new()
    {
        [FilterAction.RemoveContent] = 'r',
        [FilterAction.IssueWarning] = 'w',
        [FilterAction.SendMessage] = 'm',
        [FilterAction.ShowExplain] = 'e',
        [FilterAction.MuteModQueue] = 'u',
        [FilterAction.Kick] = 'k',
    };

    private static readonly Dictionary<char, FilterAction> CharToActionFlag = new()
    {
        ['r'] = FilterAction.RemoveContent,
        ['w'] = FilterAction.IssueWarning,
        ['m'] = FilterAction.SendMessage,
        ['e'] = FilterAction.ShowExplain,
        ['u'] = FilterAction.MuteModQueue,
        ['k'] = FilterAction.Kick,
    };
    
    public static string ToFlagsString(this FilterAction flags)
        => new(
            ActionFlagValues
                .Select(fa => flags.HasFlag(fa) ? ActionFlagToChar[fa] : '-')
                .ToArray()
        );

    public static FilterAction ToFilterAction(this string flags)
        => flags.ToCharArray()
            .Select(c => CharToActionFlag.TryGetValue(c, out var f)? f: 0)
            .Aggregate((a, b) => a | b);

    public static string GetLegend(string wrapChar = "`")
    {
        var result = new StringBuilder("Actions flag legend:");
        foreach (FilterAction fa in ActionFlagValues)
            result.Append($"\n{wrapChar}{ActionFlagToChar[fa]}{wrapChar} = {fa}");
        return result.ToString();
    }
}