using System.IO;
using System.IO.Compression;
using CompatApiClient.Utils;
using CompatBot.EventHandlers;

namespace CompatBot.Commands;

internal sealed partial class Moderation
{
//    [Command("audit"), RequiresBotModRole]
    [Description("Commands to audit server things")]
    public sealed class Audit
    {
        public static readonly SemaphoreSlim CheckLock = new(1, 1);

        /*
        [Command("spoofing"), TextAlias("impersonation"), RequiresDm]
        [Description("Checks every user on the server for name spoofing")]
        public Task Spoofing(CommandContext ctx)
        {
            SpoofingCheck(ctx);
            return Task.CompletedTask;
        }
        */

        /*
        [Command("members"), TextAlias("users"), RequiresDm]
        [Description("Dumps server member information, including usernames, nicknames, and roles")]
        public async Task Members(CommandContext ctx)
        {
            if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
            {
                await ctx.Channel.SendMessageAsync("Another check is already in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                var members = GetMembers(ctx.Client);
                await using var compressedResult = Config.MemoryStreamManager.GetStream();
                await using var memoryStream = Config.MemoryStreamManager.GetStream();
                await using var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 4096, true);
                foreach (var member in members)
                    await writer.WriteLineAsync($"{member.Username}\t{member.Nickname}\t{member.JoinedAt:O}\t{string.Join(',', member.Roles.Select(r => r.Name))}").ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                memoryStream.Seek(0, SeekOrigin.Begin);
                if (memoryStream.Length <= ctx.GetAttachmentSizeLimit())
                {
                    await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("names.txt", memoryStream)).ConfigureAwait(false);
                    return;
                }

                await using var gzip = new GZipStream(compressedResult, CompressionLevel.Optimal, true);
                await memoryStream.CopyToAsync(gzip).ConfigureAwait(false);
                await gzip.FlushAsync().ConfigureAwait(false);
                compressedResult.Seek(0, SeekOrigin.Begin);
                if (compressedResult.Length <= ctx.GetAttachmentSizeLimit())
                    await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("names.txt.gz", compressedResult)).ConfigureAwait(false);
                else
                    await ctx.Channel.SendMessageAsync($"Dump is too large: {compressedResult.Length} bytes").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to dump guild members");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to dump guild members").ConfigureAwait(false);
            }
            finally
            {
                CheckLock.Release();
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }

        [Command("raid")]
        [Description("Kick known raiders")]
        public async Task Raid(CommandContext ctx)
        {
            if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
            {
                await ctx.Channel.SendMessageAsync("Another check is already in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                var result = new StringBuilder("List of users:").AppendLine();
                var headerLength = result.Length;
                var members = GetMembers(ctx.Client);
                foreach (var member in members)
                    try
                    {
                        var displayName = member.DisplayName;
                        if (!UsernameRaidMonitor.NeedsKick(displayName))
                            continue;

                        try
                        {
                            await member.RemoveAsync("Anti Raid").ConfigureAwait(false);
                            result.AppendLine($"{member.Username} have been automatically kicked");
                        }
                        catch (Exception e)
                        {
                            Config.Log.Warn(e, $"Failed to kick member {member.GetUsernameWithNickname()}");
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to audit username for {member.Id}");
                    }
                if (result.Length == headerLength)
                    result.AppendLine("No naughty users 🎉");
                await ctx.SendAutosplitMessageAsync(result, blockStart: "", blockEnd: "").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var msg = "Failed to check display names for raids for all guild members";
                Config.Log.Warn(e, msg);
                await ctx.ReactWithAsync(Config.Reactions.Failure, msg).ConfigureAwait(false);
            }
            finally
            {
                CheckLock.Release();
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }


        [Command("zalgo"), TextAlias("diacritics")]
        [Description("Checks every member's display name for discord and rule #7 requirements")]
        public async Task Zalgo(CommandContext ctx)
        {
            if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
            {
                await ctx.Channel.SendMessageAsync("Another check is already in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                var result = new StringBuilder("List of users who do not meet Rule #7 requirements:").AppendLine();
                var headerLength = result.Length;
                var members = GetMembers(ctx.Client);
                foreach (var member in members)
                    try
                    {
                        var displayName = member.DisplayName;
                        if (!UsernameZalgoMonitor.NeedsRename(displayName))
                            continue;
                            
                        var nickname = UsernameZalgoMonitor.StripZalgo(displayName, member.Username, member.Id).Sanitize();
                        try
                        {
                            await member.ModifyAsync(m => m.Nickname = nickname).ConfigureAwait(false);
                            result.AppendLine($"{member.Mention} have been automatically renamed from {displayName} to {nickname} according Rule #7");
                        }
                        catch (Exception e)
                        {
                            Config.Log.Warn(e, $"Failed to rename member {member.GetUsernameWithNickname()}");
                            result.AppendLine($"{member.Mention} please change your nickname according to Rule #7 (suggestion: {nickname})");
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Failed to audit username for {member.Id}");
                    }
                if (result.Length == headerLength)
                    result.AppendLine("No naughty users 🎉");
                await ctx.SendAutosplitMessageAsync(result, blockStart: "", blockEnd: "").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var msg = "Failed to check display names for zalgo for all guild members";
                Config.Log.Warn(e, msg);
                await ctx.ReactWithAsync(Config.Reactions.Failure, msg).ConfigureAwait(false);
            }
            finally
            {
                CheckLock.Release();
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }
        */

/*
#if DEBUG
        [Command("locales"), TextAlias("locale", "languages", "language", "lang", "loc")]
        public async Task UserLocales(CommandContext ctx)
        {
#pragma warning disable VSTHRD103
            if (!CheckLock.Wait(0))
#pragma warning restore VSTHRD103
            {
                await ctx.Channel.SendMessageAsync("Another check is already in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                var members = GetMembers(ctx.Client);
                var stats = new Dictionary<string, int>();
                foreach (var m in members)
                {
                    var loc = m.Locale ?? "Unknown";
                    if (stats.ContainsKey(loc))
                        stats[loc]++;
                    else
                        stats[loc] = 1;
                }
                var table = new AsciiTable(
                    new AsciiColumn("Locale"),
                    new AsciiColumn("Count", alignToRight: true),
                    new AsciiColumn("%", alignToRight: true)
                );
                var total = stats.Values.Sum();
                foreach (var lang in stats.OrderByDescending(l => l.Value).ThenBy(l => l.Key))
                    table.Add(lang.Key, lang.Value.ToString(), $"{100.0 * lang.Value / total:0.00}%");
                await ctx.SendAutosplitMessageAsync(new StringBuilder().AppendLine("Member locale stats:").Append(table)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get locale stats");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to get locale stats").ConfigureAwait(false);
            }
            finally
            {
                CheckLock.Release();
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }
#endif
*/

        /*
        private static List<DiscordMember> GetMembers(DiscordClient client)
        {
            //owner -> white name
            //newbs -> veterans
            return client.Guilds.Select(g => g.Value.GetAllMembersAsync().ConfigureAwait(false))
                .SelectMany(l => l.GetAwaiter().GetResult())
                .OrderByDescending(m => m.Hierarchy)
                .ThenByDescending(m => m.JoinedAt)
                .ToList();
        }

        private static async void SpoofingCheck(CommandContext ctx)
        {
            if (!CheckLock.Wait(0))
            {
                await ctx.Channel.SendMessageAsync("Another check is already in progress").ConfigureAwait(false);
                return;
            }

            try
            {
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                var members = GetMembers(ctx.Client);
                if (members.Count < 2)
                    return;

                var result = new StringBuilder("List of potential impersonators → victims:").AppendLine();
                var headerLength = result.Length;
                var checkedMembers = new List<DiscordMember>(members.Count) {members[0]};
                for (var i = 1; i < members.Count; i++)
                {
                    var member = members[i];
                    var victims = UsernameSpoofMonitor.GetPotentialVictims(ctx.Client, member, true, true, checkedMembers);
                    if (victims.Any())
                        result.Append(member.GetMentionWithNickname()).Append(" → ").AppendLine(string.Join(", ", victims.Select(m => m.GetMentionWithNickname())));
                    checkedMembers.Add(member);
                }

                await using var compressedStream = Config.MemoryStreamManager.GetStream();
                await using var uncompressedStream = Config.MemoryStreamManager.GetStream();
                await using (var writer = new StreamWriter(uncompressedStream, new UTF8Encoding(false), 4096, true))
                {
                    await writer.WriteAsync(result.ToString()).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
                uncompressedStream.Seek(0, SeekOrigin.Begin);
                if (result.Length <= headerLength)
                {
                    await ctx.Channel.SendMessageAsync("No potential name spoofing was detected").ConfigureAwait(false);
                    return;
                }

                if (uncompressedStream.Length <= ctx.GetAttachmentSizeLimit())
                {
                    await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("spoofing_check_results.txt", uncompressedStream)).ConfigureAwait(false);
                    return;
                }

                await using (var gzip = new GZipStream(compressedStream, CompressionLevel.Optimal, true))
                {
                    await uncompressedStream.CopyToAsync(gzip).ConfigureAwait(false);
                    gzip.Flush();
                }
                compressedStream.Seek(0, SeekOrigin.Begin);
                if (compressedStream.Length <= ctx.GetAttachmentSizeLimit())
                    await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("spoofing_check_results.txt.gz", compressedStream)).ConfigureAwait(false);
                else
                    await ctx.Channel.SendMessageAsync($"Dump is too large: {compressedStream.Length} bytes").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                //should be extra careful, as async void will run on a thread pull, and will terminate the whole application with an uncaught exception
                try { await ctx.ReactWithAsync(Config.Reactions.Failure, "(X_X)").ConfigureAwait(false); } catch { }
            }
            finally
            {
                CheckLock.Release();
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
            }
        }
        */
    }
}
