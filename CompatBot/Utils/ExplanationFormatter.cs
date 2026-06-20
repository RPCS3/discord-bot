using CompatBot.Database;
using System.Text.RegularExpressions;

namespace CompatBot.Utils;

internal static partial class ExplanationFormatter
{
    [GeneratedRegex(@"</(?<name>(\w|\s)+)(:\d+)?>", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex CmdMention();

    public static async ValueTask<string> FormatTextAsync(this Explanation explanation, DiscordClient client)
    {
        if (explanation is not { Text: {Length: >0} result })
            return "";

        var matches = CmdMention().Matches(result).Select((Match m) => (m.Value, m.Groups["name"].Value)).Distinct().ToList();
        foreach (var (substr, name) in matches)
        {
            try
            {
                var nameParts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (await client.GetGlobalApplicationCommandAsync(nameParts[0]).ConfigureAwait(false) is { } cmd)
                {
                    var mention = nameParts is [_]
                        ? cmd.Mention
                        : cmd.GetSubcommandMention(nameParts[1..]);
                    result = result.Replace(substr, mention, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                continue;
            }
        }
        return result;
    }
}
