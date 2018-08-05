using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database.Providers;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal class BaseCommandModuleCustom : BaseCommandModule
    {
        public override async Task BeforeExecutionAsync(CommandContext ctx)
        {
            var disabledCmds = DisabledCommandsProvider.Get();
            if (disabledCmds.Contains(ctx.Command.QualifiedName) && !disabledCmds.Contains("*"))
            {
                await ctx.RespondAsync(embed: new DiscordEmbedBuilder {Color = Config.Colors.Maintenance, Description = "Command is currently disabled"}).ConfigureAwait(false);
                throw new DSharpPlus.CommandsNext.Exceptions.ChecksFailedException(ctx.Command, ctx, new CheckBaseAttribute[] {new RequiresDm()});
            }

            await base.BeforeExecutionAsync(ctx).ConfigureAwait(false);
        }
    }
}