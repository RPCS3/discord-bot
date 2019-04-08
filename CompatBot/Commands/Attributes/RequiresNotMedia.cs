using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes
{
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
	internal class RequiresNotMedia: CheckBaseAttribute
	{
		public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
		{
			return ctx.Channel.Name != "media";
		}
	}
}