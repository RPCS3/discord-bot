namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresSupporterRole: CheckBaseAttributeWithReactions
{
    public RequiresSupporterRole() : base(reactOnFailure: Config.Reactions.Denied) { }

    protected override async Task<bool> IsAllowed(CommandContext ctx, bool help)
        => await ctx.User.IsWhitelistedAsync(ctx.Client, ctx.Guild).ConfigureAwait(false)
           || await ctx.User.IsSupporterAsync(ctx.Client, ctx.Guild).ConfigureAwait(false);
}