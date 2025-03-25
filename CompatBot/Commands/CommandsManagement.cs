using CompatBot.Database.Providers;

namespace CompatBot.Commands;

[Command("command"), RequiresBotModRole]
[Description("Used to enable and disable bot commands at runtime")]
internal static class CommandsManagement
{
    [Command("list")]
    [Description("List the disabled commands")]
    public static async ValueTask List(SlashCommandContext ctx)
    {
        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        var list = DisabledCommandsProvider.Get();
        if (list.Count > 0)
        {
            var result = new StringBuilder("""
                Currently disabled commands:
                ```
                """);
            foreach (var cmd in list)
                result.AppendLine(cmd);
            var pages = AutosplitResponseHelper.AutosplitMessage(result.Append("```").ToString());
            await ctx.RespondAsync(pages[0], ephemeral: true).ConfigureAwait(false);
            foreach (var page in pages.Skip(1).Take(EmbedPager.MaxFollowupMessages))
                await ctx.FollowupAsync(page, ephemeral: true).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync("All commands are enabled", ephemeral: true).ConfigureAwait(false);
    }

    [Command("disable")]
    [Description("Disable the specified command")]
    public static async ValueTask Disable(
        SlashCommandContext ctx,
        [Description("Fully qualified command to disable, e.g. `explain add` or `sudo mod *`")]
        string command
    )
    {
        command ??= "";
        var isPrefix = command.EndsWith('*');
        if (isPrefix)
            command = command.TrimEnd('*', ' ');

        if (string.IsNullOrEmpty(command) && !isPrefix)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} You need to specify the command", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (ctx.Command.Parent is Command p && command.StartsWith(p.FullName))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Cannot disable command management commands", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var cmd = GetCommand(ctx, command);
        if (isPrefix)
        {
            if (cmd is null && command is {Length: >0})
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown group `{command}`", ephemeral: true).ConfigureAwait(false);
                return;
            }

            try
            {
                if (cmd is null)
                    foreach (var c in ctx.Extension.Commands.Values)
                        DisableSubcommands(ctx, c);
                else
                    DisableSubcommands(ctx, cmd);
                if (ctx.Command.Parent is Command parent && parent.FullName.StartsWith(command))
                    await ctx.RespondAsync("Some subcommands cannot be disabled", ephemeral: true).ConfigureAwait(false);
                else
                    await ctx.RespondAsync($"{Config.Reactions.Success} Disabled `{command}` and all subcommands", ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.RespondAsync($"{Config.Reactions.Failure} Error while disabling the group", ephemeral: true).ConfigureAwait(false);
            }
        }
        else
        {
            if (cmd is null)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown command `{command}`", ephemeral: true).ConfigureAwait(false);
                return;
            }

            command = cmd.FullName;
            DisabledCommandsProvider.Disable(command);
            await ctx.RespondAsync($"{Config.Reactions.Success} Disabled `{command}`", ephemeral: true).ConfigureAwait(false);
        }
    }

    [Command("enable")]
    [Description("Enable the specified command")]
    public static async ValueTask Enable(
        SlashCommandContext ctx,
        [Description("Fully qualified command to enable, e.g. `explain add` or `sudo mod *`")]
        string command
    )
    {
        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        if (command is "*")
        {
            DisabledCommandsProvider.Clear();
            await ctx.RespondAsync($"{Config.Reactions.Success} Enabled all the commands", ephemeral: true).ConfigureAwait(false);
            return;
        }

        command ??= "";
        var isPrefix = command.EndsWith('*');
        if (isPrefix)
            command = command.TrimEnd('*', ' ');

        var cmd = GetCommand(ctx, command);
        if (isPrefix)
        {
            if (cmd is null)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown group `{command}`", ephemeral: true).ConfigureAwait(false);
                return;
            }

            try
            {
                EnableSubcommands(ctx, cmd);
                await ctx.RespondAsync($"{Config.Reactions.Success} Enabled `{command}` and all subcommands", ephemeral: true).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
                await ctx.RespondAsync($"{Config.Reactions.Failure} Error while enabling the group").ConfigureAwait(false);
            }
        }
        else
        {
            if (cmd is null)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown command `{command}`", ephemeral: true).ConfigureAwait(false);
                return;
            }

            command = cmd.FullName;
            DisabledCommandsProvider.Enable(command);
            await ctx.RespondAsync($"{Config.Reactions.Success} Enabled `{command}`", ephemeral: true).ConfigureAwait(false);
        }
    }

    private static Command? GetCommand(CommandContext ctx, string qualifiedName)
    {
        if (qualifiedName is not {Length: >0})
            return null;

        IReadOnlyList<Command> groups = ctx.Extension.Commands.Values.ToList();
        Command? result = null;
        foreach (var cmdPart in qualifiedName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (groups.FirstOrDefault(g => g.Name == cmdPart
                                           // || g.Aliases.Any(a => a == cmdPart)
                                    ) is Command c)
            {
                result = c;
                if (c is Command subGroup)
                    groups = subGroup.Subcommands;
            }
            else
                return null;
        }
        return result;
    }

    private static void DisableSubcommands(CommandContext ctx, Command cmd)
    {
        if (ctx.Command.Parent is not Command p || cmd.FullName.StartsWith(p.FullName))
            return;

        DisabledCommandsProvider.Disable(cmd.FullName);
        if (cmd is Command group)
            foreach (var subCmd in group.Subcommands)
                DisableSubcommands(ctx, subCmd);
    }

    private static void EnableSubcommands(CommandContext ctx, Command cmd)
    {
        if (ctx.Command.Parent is not Command p || cmd.FullName.StartsWith(p.FullName))
            return;

        DisabledCommandsProvider.Enable(cmd.FullName);
        if (cmd is Command group)
            foreach (var subCmd in group.Subcommands)
                EnableSubcommands(ctx, subCmd);
    }
}