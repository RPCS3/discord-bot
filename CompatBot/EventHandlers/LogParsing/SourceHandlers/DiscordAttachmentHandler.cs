using System.IO.Pipelines;
using System.Net.Http;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed class DiscordAttachmentHandler : BaseSourceHandler
{
    public override async Task<Result<ISource>> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
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
                        var result = handler.CanHandle(attachment.FileName, attachment.FileSize, buf.AsSpan(0, read));
                        if (result.IsSuccess())
                            return Result.Success<ISource>(new DiscordAttachmentSource(attachment, handler, attachment.FileName, attachment.FileSize));
                        else if (result.Message is {Length: >0})
                            return result.Cast<ISource>();
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
        return Result.Failure<ISource>();
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

        public void Dispose() { }
    }
}