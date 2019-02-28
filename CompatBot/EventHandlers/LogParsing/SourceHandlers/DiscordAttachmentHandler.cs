using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class DiscordAttachmentHandler : ISourceHandler
    {
        public async Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            foreach (var attachment in message.Attachments)
                foreach (var handler in handlers)
                    if (await handler.CanHandleAsync(attachment.FileName, attachment.FileSize, attachment.Url).ConfigureAwait(false))
                        return new DiscordAttachmentSource(attachment, handler, attachment.FileName, attachment.FileSize);
            return null;
        }

        private sealed class DiscordAttachmentSource : ISource
        {
            private DiscordAttachment attachment;
            private IArchiveHandler handler;

            public string FileName { get; }
            public int FileSize { get; }

            internal DiscordAttachmentSource(DiscordAttachment attachment, IArchiveHandler handler, string fileName, int fileSize)
            {
                this.attachment = attachment;
                this.handler = handler;
                FileName = fileName;
                FileSize = fileSize;
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
