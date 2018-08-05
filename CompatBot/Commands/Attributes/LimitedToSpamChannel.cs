using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class LimitedToSpamChannel: CheckBaseAttribute
    {
        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (ctx.Channel.IsPrivate || help)
                return true;

            if (ctx.Channel.Name.Contains("spam", StringComparison.InvariantCultureIgnoreCase))
                return true;

            await ctx.RespondAsync($"`{Config.CommandPrefix}{ctx.Command.QualifiedName}` is limited to bot spam channel and DMs").ConfigureAwait(false);
            return false;
        }
    }
}