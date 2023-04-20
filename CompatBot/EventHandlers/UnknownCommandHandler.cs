using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Exceptions;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;

namespace CompatBot.EventHandlers;

internal static class UnknownCommandHandler
{
    private static readonly Regex BinaryQuestion = new(
        @"^\s*(am I|(are|is|do(es)|did|can(?!\s+of\s+)|should|must|have)(n't)?|shall|shan't|may|will|won't)\b",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    );
    
    public static Task OnError(CommandsNextExtension cne, CommandErrorEventArgs e)
    {
        OnErrorInternal(cne, e);
        return Task.CompletedTask;
    }

    public static async void OnErrorInternal(CommandsNextExtension cne, CommandErrorEventArgs e)
    {
        try
        {
            if (e.Context.User.IsBotSafeCheck())
                return;

            var ex = e.Exception;
            if (ex is InvalidOperationException && ex.Message.Contains("No matching subcommands were found"))
                ex = new CommandNotFoundException(e.Command?.Name ?? "unknown command name");

            if (ex is not CommandNotFoundException cnfe)
            {
                Config.Log.Error(e.Exception);
                return;
            }

            if (string.IsNullOrEmpty(cnfe.CommandName))
                return;

            if (e.Context.Prefix != Config.CommandPrefix
                && e.Context.Prefix != Config.AutoRemoveCommandPrefix
                && e.Context.Message.Content is string msgTxt
                && (msgTxt.EndsWith("?") || BinaryQuestion.IsMatch(msgTxt.AsSpan(e.Context.Prefix.Length)))
                && e.Context.CommandsNext.RegisteredCommands.TryGetValue("8ball", out var cmd))
            {
                var updatedContext = e.Context.CommandsNext.CreateContext(
                    e.Context.Message,
                    e.Context.Prefix,
                    cmd,
                    msgTxt[e.Context.Prefix.Length ..].Trim()
                );
                try { await cmd.ExecuteAsync(updatedContext).ConfigureAwait(false); } catch { }
                return;
            }

            var content = e.Context.Message.Content;
            if (content is null or {Length: <3})
                return;

            if (e.Context.Prefix == Config.CommandPrefix)
            {
                var knownCmds = GetAllRegisteredCommands(e.Context);
                var termParts = content.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                var normalizedTerm = string.Join(' ', termParts);
                var terms = new string[termParts.Length];
                terms[0] = termParts[0].ToLowerInvariant();
                for (var i = 1; i < termParts.Length; i++)
                    terms[i] = terms[i - 1] + ' ' + termParts[i].ToLowerInvariant();
                var cmdMatches = (
                        from t in terms
                        from kc in knownCmds
                        let v = (cmd: kc.alias, fqn: kc.fqn, w: t.GetFuzzyCoefficientCached(kc.alias), arg: normalizedTerm[t.Length ..])
                        where v.w is >0.5 and <1 // if it was a 100% match, we wouldn't be here
                        orderby v.w descending
                        select v
                    )
                    .DistinctBy(i => i.fqn)
                    .Take(4)
                    .ToList();
                var btnExplain = new DiscordButtonComponent(cmdMatches.Count == 0 ? ButtonStyle.Primary : ButtonStyle.Secondary, "unk:cmd:explain", "Explain this", emoji: new(DiscordEmoji.FromUnicode("🔍")));
                var btnCompat = new DiscordButtonComponent(ButtonStyle.Secondary, "unk:cmd:compat", "Is this game playable?", emoji: new(DiscordEmoji.FromUnicode("🔍")));
                var btnHelp = new DiscordButtonComponent(ButtonStyle.Secondary, "unk:cmd:help", "Show bot commands", emoji: new(DiscordEmoji.FromUnicode("❔")));
                var btnCancel = new DiscordButtonComponent(ButtonStyle.Danger, "unk:cmd:cancel", "Ignore", emoji: new(DiscordEmoji.FromUnicode("✖")));
                var cmdEmoji = new DiscordComponentEmoji(DiscordEmoji.FromUnicode("🤖"));
                var msgBuilder = new DiscordMessageBuilder()
                    .WithContent("I'm afraid the intended command didn't spell out quite right")
                    .AddComponents(btnExplain, btnCompat, btnHelp, btnCancel);
                if (cmdMatches.Count > 0)
                {
                    var btnSuggest = cmdMatches.Select((m, i) => new DiscordButtonComponent(i == 0 ? ButtonStyle.Primary : ButtonStyle.Secondary, "unk:cmd:s:" + m.cmd, Config.CommandPrefix + m.fqn + m.arg, emoji: cmdEmoji));
                    foreach (var btn in btnSuggest)
                        msgBuilder.AddComponents(btn);
                }
                var interactivity = cne.Client.GetInteractivity();
                var botMsg = await DiscordMessageExtensions.UpdateOrCreateMessageAsync(null, e.Context.Channel, msgBuilder).ConfigureAwait(false);
                var (_, reaction) = await interactivity.WaitForMessageOrButtonAsync(botMsg, e.Context.User, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
                string? newCmd = null, newArg = content;
                if (reaction?.Id is string btnId)
                {
                    if (btnId == btnCompat.CustomId)
                        newCmd = "c";
                    else if (btnId == btnExplain.CustomId)
                        newCmd = "explain";
                    else if (btnId == btnHelp.CustomId)
                    {
                        newCmd = "help";
                        newArg = null;
                    }
                    else if (btnId.StartsWith("unk:cmd:s:"))
                    {
                        newCmd = btnId["unk:cmd:s:".Length ..];
                        newArg = cmdMatches.First(m => m.cmd == newCmd).arg;
                    }
                }
                try { await botMsg.DeleteAsync().ConfigureAwait(false); } catch { }
                if (newCmd is not null)
                {
                    var botCommand = cne.FindCommand(newCmd, out _);
                    var commandCtx = cne.CreateContext(e.Context.Message, e.Context.Prefix, botCommand, newArg);
                    await cne.ExecuteCommandAsync(commandCtx).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            Config.Log.Error(ex);
        }
    }

    private static List<(string alias, string fqn)> GetAllRegisteredCommands(CommandContext ctx)
    {
        if (allKnownBotCommands != null)
            return allKnownBotCommands;

        static void dumpCmd(List<(string alias, string fqn)> commandList, Command cmd, string qualifiedPrefix)
        {
            foreach (var alias in cmd.Aliases.Concat(new[] {cmd.Name}))
            {
                var qualifiedAlias = qualifiedPrefix + alias;
                if (cmd is CommandGroup g)
                {
                    if (g.IsExecutableWithoutSubcommands)
                        commandList.Add((qualifiedAlias, cmd.QualifiedName));
                    dumpChildren(g, commandList, qualifiedAlias + " ");
                }
                else
                    commandList.Add((qualifiedAlias, cmd.QualifiedName));
            }
        }

        static void dumpChildren(CommandGroup group, List<(string alias, string fqn)> commandList, string qualifiedPrefix)
        {
            foreach (var cmd in group.Children)
                dumpCmd(commandList, cmd, qualifiedPrefix);
        }

        var result = new List<(string alias, string fqn)>();
        foreach (var cmd in ctx.CommandsNext.RegisteredCommands.Values)
            dumpCmd(result, cmd, "");
        allKnownBotCommands = result;
#if DEBUG
        Config.Log.Debug("Total command alias permutations: " + allKnownBotCommands.Count);
#endif
        return allKnownBotCommands;
    }

    private static List<(string alias, string fqn)>? allKnownBotCommands;
}