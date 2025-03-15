using CompatApiClient.Utils;

namespace CompatBot.Utils;

public static class AutosplitResponseHelper
{
    public static Task SendAutosplitMessageAsync(this CommandContext ctx, StringBuilder message, int blockSize = EmbedPager.MaxMessageLength, string? blockEnd = "\n```", string? blockStart = "```\n")
        => ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);

    public static ValueTask SendAutosplitMessageAsync(this CommandContext ctx, string message, int blockSize = EmbedPager.MaxMessageLength, string? blockEnd = "\n```", string? blockStart = "```\n")
        => ctx.Channel.SendAutosplitMessageAsync(message, blockSize, blockEnd, blockStart);

    public static async Task SendAutosplitMessageAsync(this DiscordChannel channel, StringBuilder message, int blockSize = EmbedPager.MaxMessageLength, string? blockEnd = "\n```", string? blockStart = "```\n")
        => await SendAutosplitMessageAsync(channel, message.ToString(), blockSize, blockEnd, blockStart).ConfigureAwait(false);

    public static async ValueTask SendAutosplitMessageAsync(this DiscordChannel channel, string message, int blockSize = EmbedPager.MaxMessageLength, string? blockEnd = "\n```", string? blockStart = "```\n")
    {
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
    
    public static List<string> AutosplitMessage(string message, int blockSize = EmbedPager.MaxMessageLength, string? blockEnd = "\n```", string? blockStart = "```\n")
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(message))
            return [];

        blockEnd ??= "";
        blockStart ??= "";
        var maxContentSize = blockSize - blockEnd.Length - blockStart.Length;
        var buffer = new StringBuilder();
        foreach (var line in message.Split(Environment.NewLine).Select(l => l.Trim(maxContentSize)))
        {
            if (buffer.Length + line.Length + blockEnd.Length > blockSize)
            {
                var content = buffer.ToString().Trim(blockSize - blockEnd.Length) + blockEnd;
                if (content.Length > blockSize)
                    Config.Log.Error($"Somehow managed to go over {blockSize} characters in a message");
                result.Add(content);
                buffer.Clear().Append(blockStart);
            }
            else
                buffer.Append('\n');
            buffer.Append(line);
        }
        var remainingContent = buffer.ToString().Trim(blockSize);
        result.Add(remainingContent);
        return result;
    }
}