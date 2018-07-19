using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class RequiresDm: CheckBaseAttribute
    {
        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (ctx.Channel.IsPrivate || help)
                return true;

            await ctx.RespondAsync($"{ctx.Message.Author.Mention} https://i.imgflip.com/24qx11.jpg").ConfigureAwait(false);
            return false;
        }
    }
}