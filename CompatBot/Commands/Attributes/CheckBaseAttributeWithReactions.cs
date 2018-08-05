using System;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands.Attributes
{
    internal abstract class CheckBaseAttributeWithReactions: CheckBaseAttribute
    {
        protected abstract Task<bool> IsAllowed(CommandContext ctx, bool help);

        public DiscordEmoji ReactOnSuccess { get; }
        public DiscordEmoji ReactOnFailure { get; }

        public CheckBaseAttributeWithReactions(DiscordEmoji reactOnSuccess = null, DiscordEmoji reactOnFailure = null)
        {
            ReactOnSuccess = reactOnSuccess;
            ReactOnFailure = reactOnFailure;
        }

        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            var result = await IsAllowed(ctx, help);
#if DEBUG
            ctx.Client.DebugLogger.LogMessage(LogLevel.Debug, "", $"Check for {GetType().Name} resulted in {result}", DateTime.Now);
#endif
            if (result)
            {
                if (ReactOnSuccess != null && !help)
                    await ctx.ReactWithAsync(ReactOnSuccess).ConfigureAwait(false);
            }
            else
            {
                if (ReactOnFailure != null && !help)
                    await ctx.ReactWithAsync(ReactOnFailure, $"{ReactOnFailure} {ctx.Message.Author.Mention} you do not have required permissions, this incident will be reported").ConfigureAwait(false);
            }
            return result;
        }
    }
}