using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class TriggersTyping: CheckBaseAttribute
    {
        public bool InDmOnly { get; set; }

        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (help)
                return true;

            if (!InDmOnly || ctx.Channel.IsPrivate)
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
            return true;
        }
    }
}