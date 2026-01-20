using System.IO;
using System.Net.Http;
using System.Reflection;
using CompatApiClient.Compression;

namespace CompatBot.Utils;

public static class DiscordMessageExtensions
{
    public static async Task<DiscordMessage> UpdateOrCreateMessageAsync(this DiscordMessage? botMsg, DiscordChannel channel, DiscordMessageBuilder messageBuilder)
    {
        Exception? lastException = null;
        for (var i = 0; i<3; i++)
            try
            {
                Task<DiscordMessage> task;
                if (botMsg is null)
                    task = channel.SendMessageAsync(messageBuilder);
                else
                {
                    if (messageBuilder.ReplyId is not null)
                    {
                        var property = messageBuilder.GetType().GetProperty(nameof(messageBuilder.ReplyId));
                        property?.SetValue(messageBuilder, null, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty, null, null, null);
                    }
                    var forceRemoveEmbed = botMsg.Embeds is {Count: >0} && messageBuilder.Embeds is not {Count: >0};
                    task = botMsg.ModifyAsync(messageBuilder, suppressEmbeds: forceRemoveEmbed);
                }
                var newMsg = await task.ConfigureAwait(false);
                if (newMsg.Channel is null)
                {
                    Config.Log.Warn("New message in DM from the bot still has no channel");
                    //newMsg.Channel = channel;
                    var property = newMsg.GetType().GetProperty(nameof(newMsg.Channel));
                    property?.SetValue(newMsg, channel, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
                    if (newMsg.Channel is null)
                        Config.Log.Error("Failed to set private field for Channel :(");
                }
                return newMsg;
            }
            catch (Exception e)
            {
                lastException = e;
                if (i == 2 || e is NullReferenceException)
                {
                    Config.Log.Error(e, "Failed to updated previous message content, will try to delete old message and create a new one");
                    if (botMsg is not null)
                        try { await botMsg.DeleteAsync().ConfigureAwait(false); } catch { }
                    return await channel.SendMessageAsync(messageBuilder).ConfigureAwait(false);
                }
                Task.Delay(100).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        throw lastException ?? new InvalidOperationException("Something gone horribly wrong");
    }

    public static Task<DiscordMessage> UpdateOrCreateMessageAsync(this DiscordMessage? botMsg, DiscordChannel channel, string? content = null, DiscordEmbed? embed = null, DiscordMessage? refMsg = null)
    {
        var msgBuilder = new DiscordMessageBuilder();
        if (content is {Length: >0})
            msgBuilder.WithContent(content);
        if (embed is not null)
            msgBuilder.AddEmbed(embed);
        if (refMsg is not null)
            msgBuilder.WithReply(refMsg.Id);
        return botMsg.UpdateOrCreateMessageAsync(channel, msgBuilder);
    }
}