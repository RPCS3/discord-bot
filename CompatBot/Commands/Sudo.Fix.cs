using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Converters;
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

        [Group("fix"), Hidden]
        [Description("Commands to fix various stuff")]
        public sealed class Fix: BaseCommandModule
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
                    await ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", "Couln't fix warning timestamps: " + e, DateTime.Now);
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }

            [Command("channels")]
            [Description("Fixes channel mentions in `warning` table")]
            public async Task Channels(CommandContext ctx)
            {
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
                try
                {
                    var @fixed = 0;
                    foreach (var warning in BotDb.Instance.Warning)
                    {
                        var newReason = await FixChannelMentionAsync(ctx, warning.Reason).ConfigureAwait(false);
                        if (newReason != warning.Reason)
                        {
                            warning.Reason = newReason;
                            @fixed++;
                        }
                    }
                    await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", "Couln't fix channel mentions: " + e, DateTime.Now);
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }

            public static async Task<string> FixChannelMentionAsync(CommandContext ctx, string msg)
            {
                if (!string.IsNullOrEmpty(msg) && msg.Contains('#'))
                {
                    var reasonParts = msg.Split(' ');
                    var rebuiltMsg = new List<string>(reasonParts.Length);
                    var changed = false;
                    foreach (var p in reasonParts)
                    {
                        if (p.Contains('#'))
                        {
                            var ch = await new CustomDiscordChannelConverter().ConvertAsync(p, ctx).ConfigureAwait(false);
                            if (ch.HasValue)
                            {
                                rebuiltMsg.Add("#" + ch.Value.Name);
                                changed = true;
                                continue;
                            }
                        }
                        rebuiltMsg.Add(p);
                    }
                    if (changed)
                        return string.Join(' ', rebuiltMsg);
                }
                return msg;
            }
        }
    }
}