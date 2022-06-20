using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Commands
{
    internal class BaseCommandModuleCustom : BaseCommandModule
    {
        private DateTimeOffset executionStart;

        public override async Task BeforeExecutionAsync(CommandContext ctx)
        {
            executionStart = DateTimeOffset.UtcNow;
            try
            {
                if (ctx.Prefix == Config.AutoRemoveCommandPrefix && ModProvider.IsMod(ctx.User.Id))
                {
                    DeletedMessagesMonitor.RemovedByBotCache.Set(ctx.Message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                    await ctx.Message.DeleteAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to delete command message with the autodelete command prefix");
            }

            if (ctx.Channel.Name == "media" && ctx.Command is { QualifiedName: not ("warn" or "report") })
            {
                Config.Log.Info($"Ignoring command from {ctx.User.Username} (<@{ctx.User.Id}>) in #media: {ctx.Message.Content}");
                if (ctx.Member is DiscordMember member)
                {
                    var dm = await member.CreateDmChannelAsync().ConfigureAwait(false);
                    await dm.SendMessageAsync($"Only `{Config.CommandPrefix}warn` and `{Config.CommandPrefix}report` are allowed in {ctx.Channel.Mention}").ConfigureAwait(false);
                }
                Config.TelemetryClient?.TrackRequest(ctx.Command.QualifiedName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Forbidden.ToString(), true);
                throw new DSharpPlus.CommandsNext.Exceptions.ChecksFailedException(ctx.Command, ctx, new CheckBaseAttribute[] { new RequiresNotMedia() });
            }

            var disabledCmds = DisabledCommandsProvider.Get();
            if (ctx.Command is not null && disabledCmds.Contains(ctx.Command.QualifiedName) && !disabledCmds.Contains("*"))
            {
                await ctx.Channel.SendMessageAsync(embed: new DiscordEmbedBuilder {Color = Config.Colors.Maintenance, Description = "Command is currently disabled"}).ConfigureAwait(false);
                Config.TelemetryClient?.TrackRequest(ctx.Command.QualifiedName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.Locked.ToString(), true);
                throw new DSharpPlus.CommandsNext.Exceptions.ChecksFailedException(ctx.Command, ctx, new CheckBaseAttribute[] {new RequiresDm()});
            }

            if (TriggersTyping(ctx))
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);

            await base.BeforeExecutionAsync(ctx).ConfigureAwait(false);
        }

        public override async Task AfterExecutionAsync(CommandContext ctx)
        {
            if (ctx.Command?.QualifiedName is string qualifiedName)
            {
                StatsStorage.CmdStatCache.TryGetValue(qualifiedName, out int counter);
                StatsStorage.CmdStatCache.Set(qualifiedName, ++counter, StatsStorage.CacheTime);
                Config.TelemetryClient?.TrackRequest(qualifiedName, executionStart, DateTimeOffset.UtcNow - executionStart, HttpStatusCode.OK.ToString(), true);
            }

            if (TriggersTyping(ctx))
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);

            await base.AfterExecutionAsync(ctx).ConfigureAwait(false);
        }

        private static bool TriggersTyping(CommandContext ctx)
            => ctx.Command?.CustomAttributes.OfType<TriggersTyping>().FirstOrDefault() is TriggersTyping a && a.ExecuteCheck(ctx);
    }
}