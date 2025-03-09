//todo: rewrite this whole thing
/*
using System.Text.RegularExpressions;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.EventArgs;
using DSharpPlus.Commands.Exceptions;
using DSharpPlus.Commands.Processors.TextCommands;

namespace CompatBot.EventHandlers;

internal static partial class UnknownCommandHandler
{
    [GeneratedRegex(
        @"^\s*(am I|(are|is|do(es)|did|can(?!\s+of\s+)|should|must|have)(n't)?|shall|shan't|may|will|won't)\b",
        RegexOptions.Singleline | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase
    )]
    private static partial Regex BinaryQuestion();
    
    public static Task OnError(CommandsExtension cne, CommandErroredEventArgs e)
    {
        OnErrorInternal(cne, e);
        return Task.CompletedTask;
    }

    public static async void OnErrorInternal(CommandsExtension cne, CommandErroredEventArgs e)
    {
        try
        {
            if (e.Context is not TextCommandContext ctx)
                return;

            if (ctx.User.IsBotSafeCheck())
                return;
           
            var ex = e.Exception;
            if (ex is InvalidOperationException && ex.Message.Contains("No matching subcommands were found"))
                ex = new CommandNotFoundException(ctx.Command.Name);

            if (ex is not CommandNotFoundException cnfe)
            {
                Config.Log.Error(e.Exception);
                return;
            }

            if (string.IsNullOrEmpty(cnfe.CommandName))
                return;

            if (ctx.Prefix != Config.CommandPrefix
                && ctx.Prefix != Config.AutoRemoveCommandPrefix
                && ctx.Message.Content is string msgTxt
                && (msgTxt.EndsWith("?") || BinaryQuestion().IsMatch(msgTxt.AsSpan(ctx.Prefix.Length)))
                && ctx.Extension.Commands.TryGetValue("8ball", out var cmd))
            {
                var updatedContext = ctx.CommandsNext.CreateContext(
                    ctx.Message,
                    ctx.Prefix,
                    cmd,
                    msgTxt[ctx.Prefix.Length ..].Trim()
                );
                try { await cmd.ExecuteAsync(updatedContext).ConfigureAwait(false); } catch { }
                return;
            }

            var content = ctx.Message.Content;
            if (content is null or {Length: <3})
                return;

            if (ctx.Prefix == Config.CommandPrefix)
            {
                var knownCmds = GetAllRegisteredCommands(ctx);
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
                var btnExplain = new DiscordButtonComponent(cmdMatches.Count == 0 ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary, "unk:cmd:explain", "Explain this", emoji: new(DiscordEmoji.FromUnicode("🔍")));
                var btnCompat = new DiscordButtonComponent(DiscordButtonStyle.Secondary, "unk:cmd:compat", "Is this game playable?", emoji: new(DiscordEmoji.FromUnicode("🔍")));
                var btnHelp = new DiscordButtonComponent(DiscordButtonStyle.Secondary, "unk:cmd:help", "Show bot commands", emoji: new(DiscordEmoji.FromUnicode("❔")));
                var btnCancel = new DiscordButtonComponent(DiscordButtonStyle.Danger, "unk:cmd:cancel", "Ignore", emoji: new(DiscordEmoji.FromUnicode("✖")));
                var cmdEmoji = new DiscordComponentEmoji(DiscordEmoji.FromUnicode("🤖"));
                var msgBuilder = new DiscordMessageBuilder()
                    .WithContent("I'm afraid the intended command didn't spell out quite right")
                    .AddComponents(btnExplain, btnCompat, btnHelp, btnCancel);
                if (cmdMatches.Count > 0)
                {
                    var btnSuggest = cmdMatches.Select((m, i) => new DiscordButtonComponent(i == 0 ? DiscordButtonStyle.Primary : DiscordButtonStyle.Secondary, "unk:cmd:s:" + m.cmd, Config.CommandPrefix + m.fqn + m.arg, emoji: cmdEmoji));
                    foreach (var btn in btnSuggest)
                        msgBuilder.AddComponents(btn);
                }
                var interactivity = cne.Client.GetInteractivity();
                var botMsg = await DiscordMessageExtensions.UpdateOrCreateMessageAsync(null, ctx.Channel, msgBuilder).ConfigureAwait(false);
                var (_, reaction) = await interactivity.WaitForMessageOrButtonAsync(botMsg, ctx.User, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
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
                    var commandCtx = cne.CreateContext(ctx.Message, ctx.Prefix, botCommand, newArg);
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
            foreach (var alias in cmd.Aliases.Concat([cmd.Name]))
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
*/