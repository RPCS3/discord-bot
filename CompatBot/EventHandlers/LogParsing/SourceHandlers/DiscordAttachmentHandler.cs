using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.Utils;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class DiscordAttachmentHandler : BaseSourceHandler
    {
        public override async Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            foreach (var attachment in message.Attachments)
            {
                try
                {
                    using (var client = HttpClientFactory.Create())
                    using (var stream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
                    {
                        var buf = bufferPool.Rent(1024);
                        try
                        {
                            var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                            foreach (var handler in handlers)
                                if (handler.CanHandle(attachment.FileName, attachment.FileSize, buf.AsSpan(0, read)))
                                    return new DiscordAttachmentSource(attachment, handler, attachment.FileName, attachment.FileSize);
                        }
                        finally
                        {
                            bufferPool.Return(buf);
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Error sniffing the rar content");
                }
            }
            return null;
        }

        private sealed class DiscordAttachmentSource : ISource
        {
            private DiscordAttachment attachment;
            private IArchiveHandler handler;

            public string SourceType => "Discord attachment";
            public string FileName { get; }
            public long SourceFileSize { get; }
            public long SourceFilePosition => handler.SourcePosition;
            public long LogFileSize => handler.LogSize;

            internal DiscordAttachmentSource(DiscordAttachment attachment, IArchiveHandler handler, string fileName, int fileSize)
            {
                this.attachment = attachment;
                this.handler = handler;
                FileName = fileName;
                SourceFileSize = fileSize;
            }

            public async Task FillPipeAsync(PipeWriter writer)
            {
                using (var client = HttpClientFactory.Create())
                using (var stream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
                    await handler.FillPipeAsync(stream, writer).ConfigureAwait(false);
            }
        }
    }
}
