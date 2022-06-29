using System;
using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class RequiresNotMedia: CheckBaseAttribute
{
	public override Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
	{
		return Task.FromResult(ctx.Channel.Name != "media");
	}
}