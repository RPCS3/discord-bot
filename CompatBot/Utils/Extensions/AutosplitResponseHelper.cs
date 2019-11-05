using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class AutosplitResponseHelper
    {
        public static Task SendAutosplitMessageAsync(this CommandContext ctx, StringBuilder message, int blockSize = 2000, string blockEnd = "\n```", string blockStart = "```\n")
        {
            return ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);
        }

        public static Task SendAutosplitMessageAsync(this CommandContext ctx, string message, int blockSize = 2000, string blockEnd = "\n```", string blockStart = "```\n")
        {
            return ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);
        }

        public static async Task SendAutosplitMessageAsync(this DiscordChannel channel, StringBuilder message, int blockSize = 2000, string blockEnd = "\n```", string blockStart = "```\n")
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            await SendAutosplitMessageAsync(channel, message.ToString(), blockSize, blockEnd, blockStart).ConfigureAwait(false);
        }

        public static async Task SendAutosplitMessageAsync(this DiscordChannel channel, string message, int blockSize = 2000, string blockEnd = "\n```", string blockStart = "```\n")
        {
            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            if (string.IsNullOrEmpty(message))
                return;

            blockEnd ??= "";
            blockStart ??= "";
            var maxContentSize = blockSize - blockEnd.Length - blockStart.Length;
            await channel.TriggerTypingAsync().ConfigureAwait(false);
            var buffer = new StringBuilder();
            foreach (var line in message.Split(Environment.NewLine).Select(l => l.Trim(maxContentSize)))
            {
                if (buffer.Length + line.Length + blockEnd.Length > blockSize)
                {
                    var content = buffer.ToString().Trim(blockSize - blockEnd.Length) + blockEnd;
                    if (content.Length > blockSize)
                        Config.Log.Error($"Somehow managed to go over {blockSize} characters in a message");
                    try
                    {
                        await channel.SendMessageAsync(content).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, "And yet the message length was " + content.Length);
                    }
                    await channel.TriggerTypingAsync().ConfigureAwait(false);
                    buffer.Clear().Append(blockStart);
                }
                else
                    buffer.Append('\n');
                buffer.Append(line);
            }
            var remainingContent = buffer.ToString().Trim(blockSize);
            try
            {
                await channel.SendMessageAsync(remainingContent).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "And yet the message length was " + remainingContent.Length);
            }
        }
    }
}
