using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    internal sealed partial class Sudo
    {
        // '2018-06-09 08:20:44.968000 - '
        // '2018-07-19T12:19:06.7888609Z - '
        private static readonly Regex Timestamp = new Regex(@"^(?<cutout>(?<date>\d{4}-\d\d-\d\d[ T][0-9:\.]+Z?) - )", RegexOptions.ExplicitCapture | RegexOptions.Singleline);

        [Group("fix")]
        [Description("Commands to fix various stuff")]
        public sealed class Fix : BaseCommandModule
        {
            [Command("timestamps")]
            [Description("Fixes `timestamp` column in the `warning` table")]
            public async Task Timestamps(CommandContext ctx)
            {
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
                try
                {
                    var @fixed = 0;
                    foreach (var warning in BotDb.Instance.Warning)
                        if (!string.IsNullOrEmpty(warning.FullReason))
                        {
                            var match = Timestamp.Match(warning.FullReason);
                            if (match.Success && DateTime.TryParse(match.Groups["date"].Value, out var timestamp))
                            {
                                warning.Timestamp = timestamp.Ticks;
                                warning.FullReason = warning.FullReason.Substring(match.Groups["cutout"].Value.Length);
                                @fixed++;
                            }
                        }
                    await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                    ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", "Couln't fix warning timestamps: " + e, DateTime.Now);
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }
        }
    }
}