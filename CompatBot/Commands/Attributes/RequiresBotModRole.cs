using CompatBot.Database.Providers;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresBotModRole: CheckBaseAttributeWithReactions
{
    public RequiresBotModRole() : base(reactOnFailure: Config.Reactions.Denied) { }

    protected override Task<bool> IsAllowed(CommandContext ctx, bool help)
        => Task.FromResult(ModProvider.IsMod(ctx.User.Id));
}