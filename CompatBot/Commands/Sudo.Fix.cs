using System.Text.RegularExpressions;
using CompatBot.Database;
using CompatBot.Database.Providers;
using DSharpPlus.Commands.Processors.TextCommands;

namespace CompatBot.Commands;

internal static partial class Sudo
{
    // '2018-06-09 08:20:44.968000 - '
    // '2018-07-19T12:19:06.7888609Z - '
    [GeneratedRegex(@"^(?<cutout>(?<date>\d{4}-\d\d-\d\d[ T][0-9:\.]+Z?) - )", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Timestamp();
    [GeneratedRegex(@"(?<id><#\d+>)", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Channel();

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
                await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
                foreach (var warning in db.Warning)
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
                await db.SaveChangesAsync().ConfigureAwait(false);
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
                await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
                foreach (var warning in db.Warning)
                {
                    var newReason = await FixChannelMentionAsync(ctx, warning.Reason).ConfigureAwait(false);
                    if (newReason != warning.Reason && newReason != null)
                    {
                        warning.Reason = newReason;
                        @fixed++;
                    }
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
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
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
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
                newTitle = newTitle.TrimEnd();
                if (newTitle == thumb.Name)
                    continue;
                    
                changed++;
                thumb.Name = newTitle;
            }
            await db.SaveChangesAsync();
            await ctx.Channel.SendMessageAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
        }

        [Command("metacritic_links"), TextAlias("mcl")]
        [Description("Cleans up Metacritic links")]
        public static async ValueTask MetacriticLinks(TextCommandContext ctx, [Description("Remove links for trial and demo versions only")] bool demosOnly = true)
        {
            var changed = 0;
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
            foreach (var thumb in db.Thumbnail.Where(t => t.MetacriticId != null))
            {
                if (demosOnly
                    && thumb.Name != null
                    && !CompatList.TrialNamePattern().IsMatch(thumb.Name))
                    continue;
                    
                thumb.MetacriticId = null;
                changed++;
            }
            await db.SaveChangesAsync();
            await ctx.Channel.SendMessageAsync($"Fixed {changed} title{(changed == 1 ? "" : "s")}").ConfigureAwait(false);
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
