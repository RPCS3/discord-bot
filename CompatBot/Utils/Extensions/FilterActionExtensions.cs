using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CompatBot.Database;

namespace CompatBot.Utils.Extensions;

internal static class FilterActionExtensions
{
    private static readonly Dictionary<FilterAction, char> ActionFlags = new()
    {
        [FilterAction.RemoveContent] = 'r',
        [FilterAction.IssueWarning] = 'w',
        [FilterAction.SendMessage] = 'm',
        [FilterAction.ShowExplain] = 'e',
        [FilterAction.MuteModQueue] = 'u',
        [FilterAction.Kick] = 'k',
    };

    public static string ToFlagsString(this FilterAction flags)
    {
        var result = Enum.GetValues(typeof(FilterAction))
            .Cast<FilterAction>()
            .Select(fa => flags.HasFlag(fa) ? ActionFlags[fa] : '-')
            .ToArray();
        return new string(result);
    }

    public static string GetLegend(string wrapChar = "`")
    {
        var result = new StringBuilder("Actions flag legend:");
        foreach (FilterAction fa in Enum.GetValues(typeof(FilterAction)))
            result.Append($"\n{wrapChar}{ActionFlags[fa]}{wrapChar} = {fa}");
        return result.ToString();
    }
}