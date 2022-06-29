using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands;

[Group("commands"), Aliases("command"), RequiresBotModRole]
[Description("Used to enable and disable bot commands at runtime")]
public sealed class CommandsManagement : BaseCommandModule
{
    [Command("list"), Aliases("show")]
    [Description("Lists the disabled commands")]
    public async Task List(CommandContext ctx)
    {
        var list = DisabledCommandsProvider.Get();
        if (list.Count > 0)
        {
            var result = new StringBuilder("Currently disabled commands:").AppendLine().AppendLine("```");
            foreach (var cmd in list)
                result.AppendLine(cmd);
            await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
        }
        else
            await ctx.Channel.SendMessageAsync("All commands are enabled").ConfigureAwait(false);
    }

    [Command("disable"), Aliases("add")]
    [Description("Disables the specified command")]
    public async Task Disable(CommandContext ctx, [RemainingText, Description("Fully qualified command to disable, e.g. `explain add` or `sudo mod *`")] string? command)
    {
        command ??= "";
        var isPrefix = command.EndsWith('*');
        if (isPrefix)
            command = command.TrimEnd('*', ' ');

        if (string.IsNullOrEmpty(command) && !isPrefix)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "You need to specify the command").ConfigureAwait(false);
            return;
        }

        if (ctx.Command?.Parent is CommandGroup p && command.StartsWith(p.QualifiedName))
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Cannot disable command management commands").ConfigureAwait(false);
            return;
        }

        var cmd = GetCommand(ctx, command);
        if (isPrefix)
        {
            if (cmd == null && !string.IsNullOrEmpty(command))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Unknown group `{command}`").ConfigureAwait(false);
                return;
            }

            try
            {
                if (cmd == null)
                    foreach (var c in ctx.CommandsNext.RegisteredCommands.Values)
                        DisableSubcommands(ctx, c);
                else
                    DisableSubcommands(ctx, cmd);
                if (ctx.Command?.Parent is CommandGroup parent && parent.QualifiedName.StartsWith(command))
                    await ctx.Channel.SendMessageAsync("Some subcommands cannot be disabled").ConfigureAwait(false);
                else
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Disabled `{command}` and all subcommands").ConfigureAwait(false);
                await List(ctx).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.Channel.SendMessageAsync("Error while disabling the group").ConfigureAwait(false);
            }
        }
        else
        {
            if (cmd == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Unknown command `{command}`").ConfigureAwait(false);
                return;
            }

            command = cmd.QualifiedName;
            DisabledCommandsProvider.Disable(command);
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Disabled `{command}`").ConfigureAwait(false);
        }
    }

    [Command("enable"), Aliases("reenable", "remove", "delete", "del", "clear")]
    [Description("Enables the specified command")]
    public async Task Enable(CommandContext ctx, [RemainingText, Description("Fully qualified command to enable, e.g. `explain add` or `sudo mod *`")] string? command)
    {
        if (command == "*")
        {
            DisabledCommandsProvider.Clear();
            await ctx.ReactWithAsync(Config.Reactions.Success, "Enabled all the commands").ConfigureAwait(false);
            return;
        }

        command ??= "";
        var isPrefix = command.EndsWith('*');
        if (isPrefix)
            command = command.TrimEnd('*', ' ');

        if (string.IsNullOrEmpty(command))
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "You need to specify the command").ConfigureAwait(false);
            return;
        }

        var cmd = GetCommand(ctx, command);
        if (isPrefix)
        {
            if (cmd == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Unknown group `{command}`").ConfigureAwait(false);
                return;
            }

            try
            {
                EnableSubcommands(ctx, cmd);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Enabled `{command}` and all subcommands").ConfigureAwait(false);
                await List(ctx).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.Channel.SendMessageAsync("Error while enabling the group").ConfigureAwait(false);
            }
        }
        else
        {
            if (cmd == null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Unknown command `{command}`").ConfigureAwait(false);
                return;
            }

            command = cmd.QualifiedName;
            DisabledCommandsProvider.Enable(command);
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Enabled `{command}`").ConfigureAwait(false);
        }
    }

    private static Command? GetCommand(CommandContext ctx, string qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return null;

        var groups = (IReadOnlyList<Command>)ctx.CommandsNext.RegisteredCommands.Values.ToList();
        Command? result = null;
        foreach (var cmdPart in qualifiedName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (groups.FirstOrDefault(g => g.Name == cmdPart || g.Aliases.Any(a => a == cmdPart)) is Command c)
            {
                result = c;
                if (c is CommandGroup subGroup)
                    groups = subGroup.Children;
            }
            else
                return null;
        }
        return result;
    }

    private static void DisableSubcommands(CommandContext ctx, Command cmd)
    {
        if (ctx.Command?.Parent is not CommandGroup p || cmd.QualifiedName.StartsWith(p.QualifiedName))
            return;

        DisabledCommandsProvider.Disable(cmd.QualifiedName);
        if (cmd is CommandGroup group)
            foreach (var subCmd in group.Children)
                DisableSubcommands(ctx, subCmd);
    }

    private static void EnableSubcommands(CommandContext ctx, Command cmd)
    {
        if (ctx.Command?.Parent is not CommandGroup p || cmd.QualifiedName.StartsWith(p.QualifiedName))
            return;

        DisabledCommandsProvider.Enable(cmd.QualifiedName);
        if (cmd is CommandGroup group)
            foreach (var subCmd in group.Children)
                EnableSubcommands(ctx, subCmd);
    }
}