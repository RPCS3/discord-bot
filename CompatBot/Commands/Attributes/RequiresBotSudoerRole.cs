﻿namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresBotSudoerRole: CheckBaseAttributeWithReactions
{
    public RequiresBotSudoerRole(): base(reactOnFailure: Config.Reactions.Denied) { }

    protected override Task<bool> IsAllowed(CommandContext ctx, bool help)
        => ctx.User.IsModeratorAsync(ctx.Client, ctx.Guild);
}