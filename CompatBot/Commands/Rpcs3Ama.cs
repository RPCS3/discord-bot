using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    [Group("ama"), Hidden]
    [Description("Provides information about the RPCS3 AMA event")]
    internal sealed class Rpcs3Ama: EventsBaseCommand
    {
        [GroupCommand]
        public Task AmaCountdown(CommandContext ctx)
            => NearestEvent(ctx, "RPCS3 AMA");

        [Command("countdown")]
        [Description("Provides information about the RPCS3 AMA event")]
        public Task Countdown(CommandContext ctx)
            => AmaCountdown(ctx);
    }
}