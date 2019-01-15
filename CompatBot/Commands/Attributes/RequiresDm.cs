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

            await ctx.RespondAsync($"{ctx.Message.Author.Mention} https://cdn.discordapp.com/attachments/417347469521715210/534798232858001418/24qx11.jpg").ConfigureAwait(false);
            return false;
        }
    }
}