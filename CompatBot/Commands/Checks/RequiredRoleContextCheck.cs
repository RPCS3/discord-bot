using CompatBot.Database.Providers;
using DSharpPlus.Commands.ContextChecks;
using DSharpPlus.Commands.Processors.TextCommands;

namespace CompatBot.Commands.Checks;

internal class RequiredRoleContextCheck:
    IContextCheck<RequiresBotModRoleAttribute>,
    IContextCheck<RequiresBotSudoerRoleAttribute>,
    IContextCheck<RequiresSupporterRoleAttribute>,
    IContextCheck<RequiresWhitelistedRoleAttribute>
{
    private async ValueTask<string?> CheckAsync<T>(T attr, CommandContext ctx, bool isAllowed)
        where T: CheckAttributeWithReactions
    {
        Config.Log.Debug($"Check for {GetType().Name} and user {ctx.User.Username}#{ctx.User.Discriminator} ({ctx.User.Id}) resulted in {isAllowed}");
        if (isAllowed)
        {
            if (ctx is TextCommandContext tctx
                && attr.ReactOnSuccess is DiscordEmoji success)
                await tctx.ReactWithAsync(success).ConfigureAwait(false);
            return null;
        }
        else
        {
            if (ctx is TextCommandContext tctx && attr.ReactOnFailure is DiscordEmoji failure)
                await tctx.ReactWithAsync(failure).ConfigureAwait(false);
            return $"{attr.ReactOnFailure} you do not have required permissions, this incident will be reported";
        }
    }

    public ValueTask<string?> ExecuteCheckAsync(RequiresBotModRoleAttribute attr, CommandContext ctx)
        => CheckAsync(attr, ctx, ModProvider.IsMod(ctx.User.Id));

    public async ValueTask<string?> ExecuteCheckAsync(RequiresBotSudoerRoleAttribute attr, CommandContext ctx)
    {
        var isAllowed = await ctx.User.IsModeratorAsync(ctx.Client, ctx.Guild).ConfigureAwait(false);
        return await CheckAsync(attr, ctx, isAllowed).ConfigureAwait(false);
    }

    public async ValueTask<string?> ExecuteCheckAsync(RequiresSupporterRoleAttribute attr, CommandContext ctx)
    {
        var isAllowed = await ctx.User.IsWhitelistedAsync(ctx.Client, ctx.Guild).ConfigureAwait(false)
                        || await ctx.User.IsSupporterAsync(ctx.Client, ctx.Guild).ConfigureAwait(false);
        return await CheckAsync(attr, ctx, isAllowed).ConfigureAwait(false);
    }

    public async ValueTask<string?> ExecuteCheckAsync(RequiresWhitelistedRoleAttribute attr, CommandContext ctx)
    {
        var isAllowed = await ctx.User.IsWhitelistedAsync(ctx.Client, ctx.Guild).ConfigureAwait(false);
        return await CheckAsync(attr, ctx, isAllowed).ConfigureAwait(false);
    }
}