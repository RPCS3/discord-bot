using System;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;

namespace CompatBot.Commands
{
    internal sealed class Moderation: BaseCommandModuleCustom
    {
        [Command("report"), RequiresWhitelistedRole]
        [Description("Adds specified message to the moderation queue")]
        public async Task Report(CommandContext ctx, [Description("Message ID from current channel to report")] ulong messageId)
        {
            try
            {
                var msg = await ctx.Channel.GetMessageAsync(messageId).ConfigureAwait(false);
                await ctx.Client.ReportAsync("Message report", msg, new[] {ctx.Message.Author}, ReportSeverity.Medium).ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, "Message reported").ConfigureAwait(false);
            }
            catch (Exception)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to report the message").ConfigureAwait(false);
            }
        }
    }
}
