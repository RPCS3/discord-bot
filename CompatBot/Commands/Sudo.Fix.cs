using System.Diagnostics;
using System.Text.RegularExpressions;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.TextCommands;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class Sudo
{
    // '2018-06-09 08:20:44.968000 - '
    // '2018-07-19T12:19:06.7888609Z - '
    [GeneratedRegex(@"^(?<cutout>(?<date>\d{4}-\d\d-\d\d[ T][0-9:\.]+Z?) - )", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"(?<id><#\d+>)", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Channel();

    [GeneratedRegex(@"rules?\s*(\d[,/ \w]*\s*)*2", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Rule2Reason();

    [Command("fix"), RequiresDm]
    [Description("Commands to fix various stuff")]
    internal static class Fix
    {
        [Command("timestamps")]
        [Description("Fix `timestamp` column in the `warning` table")]
        public static async ValueTask Timestamps(TextCommandContext ctx)
        {
            try
            {
                var @fixed = 0;
                await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                foreach (var warning in wdb.Warning)
                    if (!string.IsNullOrEmpty(warning.FullReason))
                    {
                        var match = Timestamp().Match(warning.FullReason);
                        if (match.Success && DateTime.TryParse(match.Groups["date"].Value, out var timestamp))
                        {
                            warning.Timestamp = timestamp.Ticks;
                            warning.FullReason = warning.FullReason[(match.Groups["cutout"].Value.Length)..];
                            @fixed++;
                        }
                    }
                await wdb.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Channel.SendMessageAsync($"Fixed {@fixed} records").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Couldn't fix warning timestamps");
                await ctx.Channel.SendMessageAsync("Failed to fix warning timestamps").ConfigureAwait(false);
            }
        }

        [Command("channels")]
        [Description("Fixes channel mentions in `warning` table")]
        public static async ValueTask Channels(TextCommandContext ctx)
        {
            try
            {
                var @fixed = 0;
                await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                foreach (var warning in wdb.Warning)
                {
                    var newReason = await FixChannelMentionAsync(ctx, warning.Reason).ConfigureAwait(false);
                    if (newReason != warning.Reason && newReason != null)
                    {
                        warning.Reason = newReason;
                        @fixed++;
                    }
                }
                await wdb.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Channel.SendMessageAsync($"Fixed {@fixed} records").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Couldn't fix channel mentions");
                await ctx.Channel.SendMessageAsync("Failed to fix warning timestamps").ConfigureAwait(false);
            }
        }

        [Command("syscalls")]
        [Description("Fixes invalid function names in `syscall-info` table and associated data")]
        public static async ValueTask Syscalls(TextCommandContext ctx)
        {
            try
            {
                await ctx.Channel.SendMessageAsync("Fixing invalid function names…").ConfigureAwait(false);
                var result = await SyscallInfoProvider.FixInvalidFunctionNamesAsync().ConfigureAwait(false);
                if (result.funcs > 0)
                    await ctx.Channel.SendMessageAsync($"Successfully fixed {result.funcs} function name{(result.funcs == 1 ? "" : "s")} and {result.links} game link{(result.links == 1 ? "" : "s")}").ConfigureAwait(false);
                else
                    await ctx.Channel.SendMessageAsync("No invalid syscall functions detected").ConfigureAwait(false);

                await ctx.Channel.SendMessageAsync("Fixing duplicates…").ConfigureAwait(false);
                result = await SyscallInfoProvider.FixDuplicatesAsync().ConfigureAwait(false);
                if (result.funcs > 0)
                    await ctx.Channel.SendMessageAsync($"Successfully merged {result.funcs} function{(result.funcs == 1 ? "" : "s")} and {result.links} game link{(result.links == 1 ? "" : "s")}").ConfigureAwait(false);
                else
                    await ctx.Channel.SendMessageAsync("No duplicate function entries found").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to fix syscall info");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to fix syscall information", true).ConfigureAwait(false);
            }
        }

        [Command("title_marks"), TextAlias("trademarks", "tms")]
        [Description("Strips trade marks and similar cruft from game titles in local database")]
        public static async ValueTask TitleMarks(TextCommandContext ctx)
        {
            var changed = 0;
            await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
            foreach (var thumb in wdb.Thumbnail)
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
                newTitle = newTitle.TrimEnd();
                if (newTitle == thumb.Name)
                    continue;
                    
                changed++;
                thumb.Name = newTitle;
            }
            await wdb.SaveChangesAsync();
            await ctx.Channel.SendMessageAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        [Command("metacritic_links"), TextAlias("mcl")]
        [Description("Cleans up Metacritic links")]
        public static async ValueTask MetacriticLinks(TextCommandContext ctx, [Description("Remove links for trial and demo versions only")] bool demosOnly = true)
        {
            var changed = 0;
            await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
            foreach (var thumb in wdb.Thumbnail.Where(t => t.MetacriticId != null))
            {
                if (demosOnly
                    && thumb.Name != null
                    && !CompatList.TrialNamePattern().IsMatch(thumb.Name))
                    continue;
                    
                thumb.MetacriticId = null;
                changed++;
            }
            await wdb.SaveChangesAsync();
            await ctx.Channel.SendMessageAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        [Command("amd_cpus")]
        [Description("Fixes AMD CPU models in hw.db")]
        public static async ValueTask AmdCpus(TextCommandContext ctx)
        {
            try
            {
                await using var wdb = await HardwareDb.OpenWriteAsync().ConfigureAwait(false);
                foreach (var info in wdb.HwInfo.Where(i => i.CpuMaker == "AMD" && i.CpuModel.EndsWith(" w/")))
                    info.CpuModel = info.CpuModel[..^3];
                var changed = await wdb.SaveChangesAsync().ConfigureAwait(false);
                await ctx.RespondAsync($"Updated {changed} record{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Couldn't fix AMD CPU model strings");
                await ctx.RespondAsync("Failed to fix AMD CPU strings in hw.db").ConfigureAwait(false);
            }
        }

        [Command("gpu_names")]
        [Description("Fixes GPU model names in hw.db after recent changes")]
        public static async ValueTask GpuNames(TextCommandContext ctx)
        {
            try
            {
                await using var wdb = await HardwareDb.OpenWriteAsync().ConfigureAwait(false);
                foreach (var info in wdb.HwInfo.Where(i => i.GpuModel.EndsWith("VRAM")))
                    info.GpuModel = info.GpuModel.Split(" | ")[0];
                var changed = await wdb.SaveChangesAsync().ConfigureAwait(false);
                await ctx.RespondAsync($"Updated {changed} record{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Couldn't fix GPU model strings");
                await ctx.RespondAsync("Failed to fix GPU model strings in hw.db").ConfigureAwait(false);
            }
        }

        [Command("warn_roles")]
        [Description("Try to apply warn roles retroactively")]
        public static async ValueTask WarnRoles(TextCommandContext ctx)
        {
            await ctx.RespondAsync("Checking existing warnings…").ConfigureAwait(false);
            HashSet<ulong> userIdsToWarn;
            List<Warning> warningList;
            List<ulong> usersWithWarnRole;
            await using (var rdb = await BotDb.OpenReadAsync().ConfigureAwait(false))
            {
                warningList = rdb.Warning.AsNoTracking()
                    .AsEnumerable()
                    .Where(
                        w => w.Reason.Contains("Pirated Release", StringComparison.OrdinalIgnoreCase)
                        || w.Reason.Contains("pirated game", StringComparison.OrdinalIgnoreCase)
                        || w.Reason.Contains("Piracy", StringComparison.OrdinalIgnoreCase)
                        || w.Reason.Contains('2')
                    ).ToList();
                usersWithWarnRole = await rdb.ForcedWarningRoles.AsNoTracking()
                        .Select(wr => wr.UserId)
                        .ToListAsync();
            }

            userIdsToWarn = [..
                warningList.Where(w => !w.Retracted).Select(w => w.DiscordId)
            ];

            HashSet<ulong> userIdsToFix = [..
                warningList.Where(w => w.Retracted).Select(w => w.DiscordId)
            ];
            userIdsToFix.ExceptWith(userIdsToWarn); // do not fix users with active warnings
            userIdsToWarn.ExceptWith(usersWithWarnRole); // do not assign role to users who already has the role
            userIdsToFix.IntersectWith(usersWithWarnRole); // only fix users without active warnings who still has the role

            await ctx.EditResponseAsync($"Removing role from user #1 out of {userIdsToFix.Count} (0.0%)…").ConfigureAwait(false);
            var timer = Stopwatch.StartNew();
            int processed = 1, failed=0;
            foreach (var userId in userIdsToFix)
            {
                try
                {
                    var user = await ctx.Client.GetUserAsync(userId).ConfigureAwait(false);
                    await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                    var fwr = await wdb.ForcedWarningRoles.FirstAsync(wr => wr.UserId == userId).ConfigureAwait(false);
                    wdb.ForcedWarningRoles.Remove(fwr);
                    await wdb.SaveChangesAsync().ConfigureAwait(false);
                    await user.RemoveRoleAsync(Config.WarnRoleId, ctx.Client, ctx.Guild, "Retroactive role cleanup").ConfigureAwait(false);
                    processed++;
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        await ctx.EditResponseAsync($"Removing role from user #{processed} out of {userIdsToWarn.Count} ({processed * 100.0 / userIdsToWarn.Count:0.0}%)…").ConfigureAwait(false);
                        timer.Restart();
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Failed to remove Warning role from user {userId}");
                    failed++;
                }
            }
            int totalFixed = processed - 1, failedToFix = failed;

            processed = 1;
            failed = 0;
            await ctx.EditResponseAsync($"Assigning role to user #1 out of {userIdsToWarn.Count} (0.0%)…").ConfigureAwait(false);
            foreach (var userId in userIdsToWarn)
            {
                try
                {
                    var user = await ctx.Client.GetUserAsync(userId).ConfigureAwait(false);
                    if (!await user.IsWhitelistedAsync(ctx.Client, ctx.Guild).ConfigureAwait(false) && !user.IsBot)
                    {
                        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                        await wdb.ForcedWarningRoles.AddAsync(new() { UserId = userId }).ConfigureAwait(false);
                        await wdb.SaveChangesAsync().ConfigureAwait(false);
                        await user.AddRoleAsync(Config.WarnRoleId, ctx.Client, ctx.Guild, "Retroactive role assignment").ConfigureAwait(false);
                    }
                    processed++;
                    if (timer.ElapsedMilliseconds > 10000)
                    {
                        await ctx.EditResponseAsync($"Assigning role to user #{processed} out of {userIdsToWarn.Count} ({processed * 100.0 / userIdsToWarn.Count:0.0}%)…").ConfigureAwait(false);
                        timer.Restart();
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Failed to apply Warning role retroactively to user {userId}");
                    failed++;
                }
            }

            processed--;
            await ctx.EditResponseAsync(
                $"""
                Removed role from {totalFixed} user{(totalFixed is 1 ? "" : "s")}, failed to fix {failedToFix} user{(failedToFix is 1 ? "" : "s")}.
                Assigned role to {processed} user{(processed is 1 ? "" : "s")}, failed for {failed} user{(failed is 1 ? "" : "s")}.
                """
            ).ConfigureAwait(false);
        }

        private static async ValueTask<string?> FixChannelMentionAsync(TextCommandContext ctx, string? msg)
        {
            if (msg is not {Length: >0})
                return msg;

            var entries = Channel().Matches(msg).Select(m => m.Groups["id"].Value).Distinct().ToList();
            if (entries.Count is 0)
                return msg;

            foreach (var channel in entries)
                if (await ctx.ParseChannelNameAsync(channel).ConfigureAwait(false) is {} ch)
                    msg = msg.Replace(channel, "#" + ch.Name);
            return msg;
        }
    }
}
