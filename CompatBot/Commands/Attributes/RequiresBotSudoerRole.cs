using System;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using DSharpPlus.CommandsNext;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class RequiresBotSudoerRole: CheckBaseAttributeWithReactions
    {
        public RequiresBotSudoerRole(): base(reactOnFailure: Config.Reactions.Denied) { }

        protected override Task<bool> IsAllowed(CommandContext ctx, bool help)
        {
            return Task.FromResult(ModProvider.IsSudoer(ctx.User.Id));
        }
    }
}