using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands.Attributes;

internal abstract class CheckBaseAttributeWithReactions: CheckBaseAttribute
{
    protected abstract Task<bool> IsAllowed(CommandContext ctx, bool help);

    public DiscordEmoji? ReactOnSuccess { get; }
    public DiscordEmoji? ReactOnFailure { get; }

    public CheckBaseAttributeWithReactions(DiscordEmoji? reactOnSuccess = null, DiscordEmoji? reactOnFailure = null)
    {
        ReactOnSuccess = reactOnSuccess;
        ReactOnFailure = reactOnFailure;
    }

    public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
    {
        var result = await IsAllowed(ctx, help);
        Config.Log.Debug($"Check for {GetType().Name} and user {ctx.User.Username}#{ctx.User.Discriminator} ({ctx.User.Id}) resulted in {result}");
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