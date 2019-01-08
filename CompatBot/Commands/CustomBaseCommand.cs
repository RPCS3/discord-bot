using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database.Providers;
using CompatBot.Utils;
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

            if (TriggersTyping(ctx))
                await ctx.ReactWithAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);

            await base.BeforeExecutionAsync(ctx).ConfigureAwait(false);
        }

        public override async Task AfterExecutionAsync(CommandContext ctx)
        {
            if (TriggersTyping(ctx))
                await ctx.RemoveReactionAsync(Config.Reactions.PleaseWait).ConfigureAwait(false);

            await base.AfterExecutionAsync(ctx).ConfigureAwait(false);
        }

        private static bool TriggersTyping(CommandContext ctx)
        {
            return ctx.Command.CustomAttributes.OfType<TriggersTyping>().FirstOrDefault() is TriggersTyping a && a.ExecuteCheck(ctx);
        }
    }
}