using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Commands.Converters;
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
        private static readonly Regex Channel = new Regex(@"(?<id><#\d+>)", RegexOptions.ExplicitCapture | RegexOptions.Singleline);

        [Group("fix"), Hidden, TriggersTyping]
        [Description("Commands to fix various stuff")]
        public sealed class Fix: BaseCommandModuleCustom
        {
            [Command("timestamps")]
            [Description("Fixes `timestamp` column in the `warning` table")]
            public async Task Timestamps(CommandContext ctx)
            {
                try
                {
                    var @fixed = 0;
                    using (var db = new BotDb())
                    {
                        foreach (var warning in db.Warning)
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
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    await ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Couln't fix warning timestamps");
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }

            [Command("channels")]
            [Description("Fixes channel mentions in `warning` table")]
            public async Task Channels(CommandContext ctx)
            {
                try
                {
                    var @fixed = 0;
                    using (var db = new BotDb())
                    {
                        foreach (var warning in db.Warning)
                        {
                            var newReason = await FixChannelMentionAsync(ctx, warning.Reason).ConfigureAwait(false);
                            if (newReason != warning.Reason)
                            {
                                warning.Reason = newReason;
                                @fixed++;
                            }
                        }
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    await ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Couln't fix channel mentions");
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }

            public static async Task<string> FixChannelMentionAsync(CommandContext ctx, string msg)
            {
                if (string.IsNullOrEmpty(msg))
                    return msg;

                var entries = Channel.Matches(msg).Select(m => m.Groups["id"].Value).Distinct().ToList();
                if (entries.Count == 0)
                    return msg;

                foreach (var channel in entries)
                {
                    var ch = await new TextOnlyDiscordChannelConverter().ConvertAsync(channel, ctx).ConfigureAwait(false);
                    if (ch.HasValue)
                        msg = msg.Replace(channel, "#" + ch.Value.Name);
                }
                return msg;
            }
        }
    }
}