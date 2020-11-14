using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.Utils;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class DiscordAttachmentHandler : BaseSourceHandler
    {
        public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            using var client = HttpClientFactory.Create();
            foreach (var attachment in message.Attachments)
            {
                try
                {
                    await using var stream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false);
                    var buf = BufferPool.Rent(SnoopBufferSize);
                    try
                    {
                        var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                        foreach (var handler in handlers)
                        {
                            var (canHandle, reason) = handler.CanHandle(attachment.FileName, attachment.FileSize, buf.AsSpan(0, read));
                            if (canHandle)
                                return (new DiscordAttachmentSource(attachment, handler, attachment.FileName, attachment.FileSize), null);
                            else if (!string.IsNullOrEmpty(reason))
                                return (null, reason);
                        }
                    }
                    finally
                    {
                        BufferPool.Return(buf);
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Error sniffing the rar content");
                }
            }
            return (null, null);
        }

        private sealed class DiscordAttachmentSource : ISource
        {
            private readonly DiscordAttachment attachment;
            private readonly IArchiveHandler handler;

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

            public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
            {
                using var client = HttpClientFactory.Create();
                await using var stream = await client.GetStreamAsync(attachment.Url, cancellationToken).ConfigureAwait(false);
                await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
