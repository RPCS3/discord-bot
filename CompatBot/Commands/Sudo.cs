using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    [Group("sudo"), RequiresBotSudoerRole]
    [Description("Used to manage bot moderators and sudoers")]
    internal sealed partial class Sudo : BaseCommandModuleCustom
    {
        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public async Task Say(CommandContext ctx, [Description("Discord channel (can use just #name in DM)")] DiscordChannel channel, [RemainingText, Description("Message text to send")] string message)
        {
            var typingTask = channel.TriggerTypingAsync();
            // simulate bot typing the message at 300 cps
            await Task.Delay(message.Length * 10 / 3).ConfigureAwait(false);
            await channel.SendMessageAsync(message).ConfigureAwait(false);
            await typingTask.ConfigureAwait(false);
        }

        [Command("say"), Priority(10)]
        [Description("Make bot say things, optionally in a specific channel")]
        public Task Say(CommandContext ctx, [RemainingText, Description("Message text to send")] string message)
        {
            return Say(ctx, ctx.Channel, message);
        }
    }
}
