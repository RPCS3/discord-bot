using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed partial class Moderation
    {
        [Group("audit"), RequiresBotModRole]
        [Description("Commands to audit server things")]
        public sealed class Audit: BaseCommandModuleCustom
        {
            private static readonly SemaphoreSlim CheckLock = new SemaphoreSlim(1, 1);

            [Command("spoofing"), Aliases("impersonation"), RequireDirectMessage]
            [Description("Checks every user on the server for name spoofing")]
            public Task Spoofing(CommandContext ctx)
            {
                SpoofingCheck(ctx);
                return Task.CompletedTask;
            }

            [Command("members"), Aliases("users"), RequireDirectMessage]
            [Description("Dumps server member information, including usernames, nicknames, and roles")]
            public async Task Members(CommandContext ctx)
            {
                if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
                {
                    await ctx.RespondAsync("Another check is already in progress").ConfigureAwait(false);
                    return;
                }

                try
                {
                    await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                    var members = GetMembers(ctx.Client);
                    using (var compressedResult = new MemoryStream())
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 4096, true))
                            {
                                foreach (var member in members)
                                    await writer.WriteLineAsync($"{member.Username}\t{member.Nickname}\t{member.JoinedAt:O}\t{(string.Join(',', member.Roles.Select(r => r.Name)))}").ConfigureAwait(false);
                                await writer.FlushAsync().ConfigureAwait(false);
                            }
                            memoryStream.Seek(0, SeekOrigin.Begin);
                            if (memoryStream.Length <= Config.AttachmentSizeLimit)
                            {
                                await ctx.RespondWithFileAsync("names.txt", memoryStream).ConfigureAwait(false);
                                return;
                            }

                            using (var gzip = new GZipStream(compressedResult, CompressionLevel.Optimal, true))
                            {
                                await memoryStream.CopyToAsync(gzip).ConfigureAwait(false);
                                await gzip.FlushAsync().ConfigureAwait(false);
                            }
                        }
                        compressedResult.Seek(0, SeekOrigin.Begin);
                        if (compressedResult.Length <= Config.AttachmentSizeLimit)
                            await ctx.RespondWithFileAsync("names.txt.gz", compressedResult).ConfigureAwait(false);
                        else
                            await ctx.RespondAsync($"Dump is too large: {compressedResult.Length} bytes").ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to dump guild members");
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to dump guild members").ConfigureAwait(false);
                }
                finally
                {
                    CheckLock.Release(1);
                    await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                }
            }

            [Command("zalgo"), Aliases("diacritics")]
            [Description("Checks every member's display name for discord and rule #7 requirements")]
            public async Task Zalgo(CommandContext ctx)
            {
                if (!await CheckLock.WaitAsync(0).ConfigureAwait(false))
                {
                    await ctx.RespondAsync("Another check is already in progress").ConfigureAwait(false);
                    return;
                }

                try
                {
                    await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                    var result = new StringBuilder("List of users who do not meet Rule #7 requirements:");
                    var headerLength = result.Length;
                    var members = GetMembers(ctx.Client);
                    foreach (var member in members)
                        if (UsernameZalgoMonitor.NeedsRename(member.DisplayName))
                            result.AppendLine($"{member.Mention} please change your nickname according to Rule #7");
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
                    CheckLock.Release(1);
                    await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                }
            }

            /*
            [Command("locales"), Aliases("locale", "languages", "language", "lang", "loc")]
            public async Task UserLocales(CommandContext ctx)
            {
                if (!CheckLock.Wait(0))
                {
                    await ctx.RespondAsync("Another check is already in progress").ConfigureAwait(false);
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
                    CheckLock.Release(1);
                    await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                }
            }
            */

            private List<DiscordMember> GetMembers(DiscordClient client)
            {
                //owner -> white name
                //newbs -> veterans
                return client.Guilds.Select(g => g.Value.GetAllMembersAsync().ConfigureAwait(false))
                    .SelectMany(l => l.GetAwaiter().GetResult())
                    .OrderByDescending(m => m.Hierarchy)
                    .ThenByDescending(m => m.JoinedAt)
                    .ToList();
            }

            private async void SpoofingCheck(CommandContext ctx)
            {
                if (!CheckLock.Wait(0))
                {
                    await ctx.RespondAsync("Another check is already in progress").ConfigureAwait(false);
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

                    using (var compressedStream = new MemoryStream())
                    {
                        using (var uncompressedStream = new MemoryStream())
                        {
                            using (var writer = new StreamWriter(uncompressedStream, new UTF8Encoding(false), 4096, true))
                            {
                                writer.Write(result.ToString());
                                writer.Flush();
                            }
                            uncompressedStream.Seek(0, SeekOrigin.Begin);
                            if (result.Length <= headerLength)
                            {
                                await ctx.RespondAsync("No potential name spoofing was detected").ConfigureAwait(false);
                                return;
                            }

                            if (uncompressedStream.Length <= Config.AttachmentSizeLimit)
                            {
                                await ctx.RespondWithFileAsync("spoofing_check_results.txt", uncompressedStream).ConfigureAwait(false);
                                return;
                            }

                            using (var gzip = new GZipStream(compressedStream, CompressionLevel.Optimal, true))
                            {
                                uncompressedStream.CopyTo(gzip);
                                gzip.Flush();
                            }
                            compressedStream.Seek(0, SeekOrigin.Begin);
                            if (compressedStream.Length <= Config.AttachmentSizeLimit)
                                await ctx.RespondWithFileAsync("spoofing_check_results.txt.gz", compressedStream).ConfigureAwait(false);
                            else
                                await ctx.RespondAsync($"Dump is too large: {compressedStream.Length} bytes").ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e);
                    //should be extra careful, as async void will run on a thread pull, and will terminate the whole application with an uncaught exception
                    try { await ctx.ReactWithAsync(Config.Reactions.Failure, "(X_X)").ConfigureAwait(false); } catch { }
                }
                finally
                {
                    CheckLock.Release(1);
                    await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);
                }
            }
        }
    }
}
