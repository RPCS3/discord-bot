using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class AutosplitResponseHelper
    {
        public static Task SendAutosplitMessageAsync(this CommandContext ctx, StringBuilder message, int blockSize = 2000, string blockEnd = "```", string blockStart = "```")
        {
            return ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);
        }

        public static Task SendAutosplitMessageAsync(this CommandContext ctx, string message, int blockSize = 2000, string blockEnd = "```", string blockStart = "```")
        {
            return ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);
        }

        public static async Task SendAutosplitMessageAsync(this DiscordChannel channel, StringBuilder message, int blockSize = 2000, string blockEnd = "```", string blockStart = "```")
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await SendAutosplitMessageAsync(channel, message.ToString(), blockSize, blockEnd, blockStart).ConfigureAwait(false);
        }

        public static async Task SendAutosplitMessageAsync(this DiscordChannel channel, string message, int blockSize = 2000, string blockEnd = "```", string blockStart = "```")
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            if (string.IsNullOrEmpty(message))
                return;

            blockEnd = blockEnd ?? "";
            blockStart = blockStart ?? "";
            var maxContentSize = blockSize - blockEnd.Length - blockStart.Length;
            await channel.TriggerTypingAsync().ConfigureAwait(false);
            var buffer = new StringBuilder();
            foreach (var line in message.Split(Environment.NewLine).Select(l => l.Trim(maxContentSize)))
            {
                if (buffer.Length + line.Length + blockEnd.Length > blockSize)
                {
                    await channel.SendMessageAsync(buffer.Append(blockEnd).ToString()).ConfigureAwait(false);
                    await channel.TriggerTypingAsync().ConfigureAwait(false);
                    buffer.Clear().Append(blockStart);
                }
                else
                    buffer.AppendLine();
                buffer.Append(line);
            }
            await channel.SendMessageAsync(buffer.ToString()).ConfigureAwait(false);
        }
    }
}
