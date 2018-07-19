using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Attributes
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
            //await ctx.RespondAsync($"Check for {GetType().Name} resulted in {result}").ConfigureAwait(false);
            if (result)
            {
                if (ReactOnSuccess != null && !help)
                    await ctx.Message.CreateReactionAsync(ReactOnSuccess).ConfigureAwait(false);
            }
            else
            {
                if (ReactOnFailure != null && !help)
                    await ctx.Message.CreateReactionAsync(ReactOnFailure).ConfigureAwait(false);
            }
            return result;
        }
    }
}