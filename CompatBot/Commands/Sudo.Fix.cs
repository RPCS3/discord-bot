using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
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

        [Group("fix"), Hidden]
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
                                    warning.FullReason = warning.FullReason[(match.Groups["cutout"].Value.Length)..];
                                    @fixed++;
                                }
                            }
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    await ctx.RespondAsync($"Fixed {@fixed} records").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Couldn't fix warning timestamps");
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
                    Config.Log.Warn(e, "Couldn't fix channel mentions");
                    await ctx.RespondAsync("Failed to fix warning timestamps").ConfigureAwait(false);
                }
            }

            [Command("syscalls")]
            [Description("Fixes invalid function names in `syscall-info` table and associated data")]
            public async Task Syscalls(CommandContext ctx)
            {
                try
                {
                    await ctx.RespondAsync("Fixing invalid function names...").ConfigureAwait(false);
                    var result = await SyscallInfoProvider.FixInvalidFunctionNamesAsync().ConfigureAwait(false);
                    if (result.funcs > 0)
                        await ctx.RespondAsync($"Successfully fixed {result.funcs} function name{(result.funcs == 1 ? "" : "s")} and {result.links} game link{(result.links == 1 ? "" : "s")}").ConfigureAwait(false);
                    else
                        await ctx.RespondAsync("No invalid syscall functions detected").ConfigureAwait(false);

                    await ctx.RespondAsync("Fixing duplicates...").ConfigureAwait(false);
                    result = await SyscallInfoProvider.FixDuplicatesAsync().ConfigureAwait(false);
                    if (result.funcs > 0)
                        await ctx.RespondAsync($"Successfully merged {result.funcs} function{(result.funcs == 1 ? "" : "s")} and {result.links} game link{(result.links == 1 ? "" : "s")}").ConfigureAwait(false);
                    else
                        await ctx.RespondAsync("No duplicate function entries found").ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to fix syscall info");
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to fix syscall information", true).ConfigureAwait(false);
                }
            }

            [Command("title_marks"), Aliases("trademarks", "tms")]
            [Description("Strips trade marks and similar cruft from game titles in local database")]
            public async Task TitleMarks(CommandContext ctx)
            {
                var changed = 0;
                using var db = new ThumbnailDb();
                foreach (var thumb in db.Thumbnail)
                {
                    if (string.IsNullOrEmpty(thumb.Name))
                        continue;

                    var newTitle = thumb.Name.StripMarks();
                    if (newTitle.EndsWith("full game", StringComparison.OrdinalIgnoreCase))
                        newTitle = newTitle[..^10];
                    if (newTitle.EndsWith("full game unlock", StringComparison.OrdinalIgnoreCase))
                        newTitle = newTitle[..^17];
                    if (newTitle.EndsWith("downloadable game", StringComparison.OrdinalIgnoreCase))
                        newTitle = newTitle[..^18];
                    newTitle.TrimEnd();
                    if (newTitle != thumb.Name)
                    {
                        changed++;
                        thumb.Name = newTitle;
                    }
                }
                await db.SaveChangesAsync();
                await ctx.RespondAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
            }

            [Command("metacritic_links"), Aliases("mcl")]
            [Description("Cleans up Metacritic links")]
            public async Task MetacriticLinks(CommandContext ctx, [Description("Remove links for trial and demo versions only")] bool demosOnly = true)
            {
                var changed = 0;
                using var db = new ThumbnailDb();
                foreach (var thumb in db.Thumbnail.Where(t => t.MetacriticId != null))
                {
                    if (!demosOnly || Regex.IsMatch(thumb.Name, @"\b(demo|trial)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                        thumb.MetacriticId = null;

                }
                await db.SaveChangesAsync();
                await ctx.RespondAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
            }

            public static async Task<string?> FixChannelMentionAsync(CommandContext ctx, string? msg)
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