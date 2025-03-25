using System.Diagnostics.CodeAnalysis;
using System.Net;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using DSharpPlus.Commands.Processors.TextCommands;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Commands.Processors;

public class CustomCommandExecutor: DefaultCommandExecutor
{
    private DateTimeOffset executionStart;
    
    public override async ValueTask ExecuteAsync(CommandContext ctx, CancellationToken cancellationToken = default)
    {
        executionStart = DateTimeOffset.UtcNow;
        try
        {
            if (ctx is TextCommandContext tctx
                && tctx.Prefix == Config.AutoRemoveCommandPrefix
                && ModProvider.IsMod(ctx.User.Id))
            {
                DeletedMessagesMonitor.RemovedByBotCache.Set(tctx.Message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                await tctx.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to delete command message with the autodelete command prefix");
        }
        
        await base.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        var qualifiedName = ctx.Command.FullName;
        StatsStorage.IncCmdStat(qualifiedName);
        Config.TelemetryClient?.TrackRequest(qualifiedName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.OK.ToString(), true);
    }
    
    protected override bool IsCommandExecutable(CommandContext ctx, [NotNullWhen(false)] out string? errorMessage)
    {
        var disabledCmds = DisabledCommandsProvider.Get();
        if (disabledCmds.Contains(ctx.Command.FullName) && !disabledCmds.Contains("*"))
        {
            //Config.TelemetryClient?.TrackRequest(ctx.Command.FullName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Locked.ToString(), true);
            errorMessage = "Command is currently disabled";
            return false;
        }
        
        if (ctx.Channel.Name is "media" && ctx.Command is { FullName: not ("warning give" or "👮 Report to mods") })
        {
            //Config.TelemetryClient?.TrackRequest(ctx.Command.FullName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Forbidden.ToString(), true);
            Config.Log.Info($"Ignoring command from {ctx.User.Username} (<@{ctx.User.Id}>) in #media: {ctx.Command}");
            errorMessage = $"Please use #bot-spam for bot commands";
            return false;
        }

        
        errorMessage = null;
        return true;
    }
}