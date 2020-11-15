using System.Threading.Tasks;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    [Group("cp77"), Aliases("cp2077", "cyberpunk2077", "cyberpunk"), Hidden]
    [Description("Provides information about the Cyberpunk 2077 release event")]
    internal sealed class Cyberpunk2077: EventsBaseCommand
    {
        [GroupCommand]
        public Task Cp77Countdown(CommandContext ctx)
            => NearestEvent(ctx, "Cyberpunk 2077");

        [Command("2077"), Hidden]
        public Task Cp77(CommandContext ctx, [RemainingText] string? _ = null)
            => NearestEvent(ctx, "Cyberpunk 2077");

        [Command("countdown")]
        [Description("Provides countdown for Cyberpunk 2077 release event")]
        public Task Countdown(CommandContext ctx)
            => Cp77Countdown(ctx);
    }
}