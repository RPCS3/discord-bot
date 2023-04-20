using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using CompatBot.Utils;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed class PastebinHandler : BaseSourceHandler
{
    private static readonly Regex ExternalLink = new(@"(?<pastebin_link>(https?://)pastebin.com/(raw/)?(?<pastebin_id>[^/>\s]+))", DefaultOptions);

    public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
    {
        if (string.IsNullOrEmpty(message.Content))
            return (null, null);

        var matches = ExternalLink.Matches(message.Content);
        if (matches.Count == 0)
            return (null, null);

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
                        var (canHandle, reason) = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                        if (canHandle)
                            return (new PastebinSource(uri, filename, filesize, handler), null);
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
                Config.Log.Warn(e, $"Error sniffing {m.Groups["mega_link"].Value}");
            }
        }
        return (null, null);
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