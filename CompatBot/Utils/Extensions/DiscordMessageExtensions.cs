using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    public static class DiscordMessageExtensions
    {
        public static async Task<DiscordMessage> UpdateOrCreateMessageAsync(this DiscordMessage? message, DiscordChannel channel, string? content = null, DiscordEmbed? embed = null)
        {
            Exception? lastException = null;
            for (var i = 0; i<3; i++)
                try
                {
                    if (message == null)
                    {
                        var newMsg = await channel.SendMessageAsync(content, embed).ConfigureAwait(false);
                        #warning Ugly hack, needs proper fix in upstream, but they are not enthused to do so
                        if (newMsg.Channel is null)
                        {
                            //newMsg.Channel = channel;
                            var property = newMsg.GetType().GetProperty(nameof(newMsg.Channel));
                            property?.SetValue(newMsg, channel, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
                        }
                        return newMsg;
                    }
                    return await message.ModifyAsync(content, embed).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    lastException = e;
                    if (i == 2)
                        Config.Log.Error(e);
                    else
                        Task.Delay(100).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            throw lastException ?? new InvalidOperationException("Something gone horribly wrong");
        }

        public static async Task<(Dictionary<string, Stream>? attachmentContent, List<string>? failedFilenames)> DownloadAttachmentsAsync(this DiscordMessage msg)
        {
            if (msg.Attachments.Count == 0)
                return (null, null);

            var attachmentContent = new Dictionary<string, Stream>(msg.Attachments.Count);
            var attachmentFilenames = new List<string>();
            using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
            foreach (var att in msg.Attachments)
            {
                if (att.FileSize > Config.AttachmentSizeLimit)
                {
                    attachmentFilenames.Add(att.FileName);
                    continue;
                }

                try
                {
                    await using var sourceStream = await httpClient.GetStreamAsync(att.Url).ConfigureAwait(false);
                    var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);
                    await sourceStream.CopyToAsync(fileStream, 16384, Config.Cts.Token).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    attachmentContent[att.FileName] = fileStream;
                }
                catch (Exception ex)
                {
                    Config.Log.Warn(ex, $"Failed to download attachment {att.FileName} from deleted message {msg.JumpLink}");
                    attachmentFilenames.Add(att.FileName);
                }
            }
            return (attachmentContent: attachmentContent, failedFilenames: attachmentFilenames);
        }
    }
}
