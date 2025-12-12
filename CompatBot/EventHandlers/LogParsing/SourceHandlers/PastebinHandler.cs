using System.IO.Pipelines;
using System.Net.Http;
using System.Text.RegularExpressions;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed partial class PastebinHandler : BaseSourceHandler
{
    [GeneratedRegex(@"(?<pastebin_link>(https?://)pastebin.com/(raw/)?(?<pastebin_id>[^/>\s]+))", DefaultOptions)]
    private static partial Regex ExternalLink();

    public override async Task<Result<ISource>> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
    {
        if (message.Content is not {Length: >0})
            return Result.Failure<ISource>();

        var matches = ExternalLink().Matches(message.Content);
        if (matches is [])
            return Result.Failure<ISource>();

        using var client = HttpClientFactory.Create();
        foreach (Match m in matches)
        {
            try
            {
                if (m.Groups["pastebin_id"].Value is not { Length: > 0 } pid)
                    continue;
                
                var uri = new Uri("https://pastebin.com/raw/" + pid);
                await using var stream = await client.GetStreamAsync(uri).ConfigureAwait(false);
                var buf = BufferPool.Rent(SnoopBufferSize);
                try
                {
                    var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                    var filename = pid + ".log";
                    var filesize = stream.CanSeek ? (int)stream.Length : 0;
                    foreach (var handler in handlers)
                    {
                        var result = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                        if (result.IsSuccess())
                            return Result.Success<ISource>(new PastebinSource(uri, filename, filesize, handler));
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
                Config.Log.Warn(e, $"Error sniffing {m.Groups["mega_link"].Value}");
            }
        }
        return Result.Failure<ISource>();
    }

    private sealed class PastebinSource : ISource
    {
        private readonly Uri uri;
        private readonly IArchiveHandler handler;
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        public PastebinSource(Uri uri, string filename, int filesize, IArchiveHandler handler)
        {
            this.uri = uri;
            FileName = filename;
            SourceFileSize = filesize;
            this.handler = handler;
        }

        public string SourceType => "Pastebin";
        public string FileName { get; }
        public long SourceFileSize { get; }

        public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            using var client = HttpClientFactory.Create();
            await using var stream = await client.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
            await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() { }
    }
}