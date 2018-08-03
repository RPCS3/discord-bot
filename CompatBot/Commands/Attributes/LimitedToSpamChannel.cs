using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class LimitedToSpamChannel: CheckBaseAttribute
    {
        public override Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (ctx.Channel.IsPrivate || help)
                return Task.FromResult(true);

            if (ctx.Channel.Name.Contains("spam", StringComparison.InvariantCultureIgnoreCase))
                return Task.FromResult(true);

            return Task.FromResult(false);
        }
    }
}