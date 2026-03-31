using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using CompatApiClient.Utils;

namespace CompatBot.Utils;

public static partial class DiscordMessageExtensions
{
    [GeneratedRegex(@"<(?<lnk>(?!(#|@[&!]?\d+))[^>]+)>", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Link();
    
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

    public static async ValueTask<string> GetMessageContentForFiltersAsync(this DiscordMessage message, DiscordClient client, bool includeEmbeds = true, bool includeAttachments = true)
    {
        var content = new StringBuilder().Append(message, includeEmbeds, includeAttachments);
        if (message.Reference is { Type: DiscordMessageReferenceType.Forward } refMsg)
        {
            try
            {
                if (await client.GetMessageAsync(refMsg.Channel, refMsg.Message.Id).ConfigureAwait(false) is {} msg)
                    content.AppendLine().Append(msg);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get forwarded message");
            }
        }
        return content.ToString();
    }

    private static StringBuilder Append(this StringBuilder content, DiscordMessage message, bool includeEmbeds = true, bool includeAttachments = true)
    {
        if (message.Content is { Length: > 0 })
            content.AppendLine(message.Content.FixSuppressedLinks());
        if (includeAttachments)
            foreach (var attachment in message.Attachments.Where(a => a.FileName is { Length: > 0 }))
                content.AppendLine(attachment.FileName);
        if (includeEmbeds)
            foreach (var embed in message.Embeds)
            {
                content.AppendLine(embed.Title).AppendLine(embed.Description);
                if (embed.Fields is not { Count: >0 })
                    continue;

                foreach (var field in embed.Fields)
                {
                    content.AppendLine(field.Name);
                    content.AppendLine(field.Value);
                }
            }
        return content;
    }

    [return: NotNullIfNotNull(nameof(content))]
    private static string? FixSuppressedLinks(this string? content)
    {
        if (content is not { Length: > 0 })
            return content;
        
        var matches = Link().Matches(content);
        foreach (Match m in matches)
        {
            var lnk = m.Groups["lnk"].Value;
            var fixedLnk = Uri.UnescapeDataString(lnk.RemoveWhitespaces());
            content = content.Replace(lnk, fixedLnk);
        }
        return content;
    }
}