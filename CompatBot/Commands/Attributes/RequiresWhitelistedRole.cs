using System;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class RequiresWhitelistedRole: CheckBaseAttributeWithReactions
    {
        public RequiresWhitelistedRole() : base(reactOnFailure: Config.Reactions.Denied) { }

        protected override Task<bool> IsAllowed(CommandContext ctx, bool help)
        {
            return Task.FromResult(ctx.User.IsWhitelisted(ctx.Client, ctx.Guild));
        }
    }
}