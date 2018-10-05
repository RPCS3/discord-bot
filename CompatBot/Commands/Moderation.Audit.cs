using System;
using System.Collections.Generic;
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

            private async void SpoofingCheck(CommandContext ctx)
            {
                if (!CheckLock.Wait(0))
                {
                    await ctx.RespondAsync("Another check is already in progress").ConfigureAwait(false);
                    return;
                }

                try
                {
                    await ctx.TriggerTypingAsync().ConfigureAwait(false);
                    //owner -> white name
                    //newbs -> veterans
                    var members = ctx.Client.Guilds.SelectMany(g => g.Value.Members)
                        .OrderByDescending(m => m.Hierarchy)
                        .ThenByDescending(m => m.JoinedAt)
                        .ToList();
                    if (members.Count < 2)
                        return;

                    var result = new StringBuilder("List of potential impersonators -> victims:").AppendLine();
                    var headerLength = result.Length;
                    var checkedMembers = new List<DiscordMember>(members.Count) {members[0]};
                    for (var i = 1; i < members.Count; i++)
                    {
                        var member = members[i];
                        var victims = UsernameSpoofMonitor.GetPotentialVictims(ctx.Client, member, true, true, checkedMembers);
                        if (victims.Any())
                            result.Append(member.GetMentionWithNickname()).Append(" -> ").AppendLine(string.Join(", ", victims.Select(m => m.GetMentionWithNickname())));
                        checkedMembers.Add(member);
                    }
                    if (result.Length > headerLength)
                        await ctx.SendAutosplitMessageAsync(result, blockEnd: null, blockStart: null).ConfigureAwait(false);
                    else
                        await ctx.RespondAsync("No potential name spoofing was detected").ConfigureAwait(false);
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
                }
            }
        }
    }
}
