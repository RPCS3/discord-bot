using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CompatBot.Database;

namespace CompatBot.Utils.Extensions
{
    internal static class FilterActionExtensions
    {
        public static readonly Dictionary<FilterAction, char> ActionFlags = new Dictionary<FilterAction, char>
        {
            [FilterAction.RemoveMessage] = 'r',
            [FilterAction.IssueWarning] = 'w',
            [FilterAction.SendMessage] = 'm',
            [FilterAction.ShowExplain] = 'e',
        };

        public static string ToFlagsString(this FilterAction flags)
        {
            var result = Enum.GetValues(typeof(FilterAction))
                .Cast<FilterAction>()
                .Select(fa => flags.HasFlag(fa) ? ActionFlags[fa] : '-')
                .ToArray();
            return new string(result);
        }

        public static string GetLegend()
        {
            var result = new StringBuilder("Actions flag legend:");
            foreach (FilterAction fa in Enum.GetValues(typeof(FilterAction)))
                result.Append($"\n{ActionFlags[fa]} = {fa}");
            return result.ToString();
        }
    }
}
