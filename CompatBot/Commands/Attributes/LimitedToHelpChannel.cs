using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class LimitedToHelpChannel: CheckBaseAttribute
    {
        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (ctx.Channel.IsPrivate || help)
                return true;

            if (ctx.Channel.Name.Equals("help", StringComparison.InvariantCultureIgnoreCase))
                return true;

            await ctx.Channel.SendMessageAsync($"`{ctx.Prefix}{ctx.Command.QualifiedName}` is limited to help channel and DMs").ConfigureAwait(false);
            return false;
        }
    }
}