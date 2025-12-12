using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed partial class DropboxHandler : BaseSourceHandler
{
    //https://www.dropbox.com/s/62ls9lw5i52fuib/RPCS3.log.gz?dl=0
    [GeneratedRegex(@"(?<dropbox_link>(https?://)?(www\.)?dropbox\.com/s/(?<dropbox_id>[^/\s]+)/(?<filename>[^/\?\s])(/dl=[01])?)", DefaultOptions)]
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
            if (m.Groups["dropbox_link"].Value is not { Length: >0 } lnk
                || !Uri.TryCreate(lnk, UriKind.Absolute, out var uri))
                continue;
            
            try
            {
                uri = uri.SetQueryParameter("dl", "1");
                var filename = Path.GetFileName(lnk);
                var filesize = -1;

                using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Config.Cts.Token);
                    if (response.Content.Headers.ContentLength > 0)
                        filesize = (int)response.Content.Headers.ContentLength.Value;
                    if (response.Content.Headers.ContentDisposition?.FileNameStar is {Length: >0} fname)
                        filename = fname;
                    uri = response.RequestMessage?.RequestUri;
                }

                await using var stream = await client.GetStreamAsync(uri).ConfigureAwait(false);
                var buf = BufferPool.Rent(SnoopBufferSize);
                try
                {
                    var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                    foreach (var handler in handlers)
                    {
                        var result = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                        if (result.IsSuccess())
                            return Result.Success<ISource>(new DropboxSource(uri, handler, filename, filesize));
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
                Config.Log.Warn(e, $"Error sniffing {m.Groups["dropbox_link"].Value}");
            }
        }
        return Result.Failure<ISource>();
    }

    private sealed class DropboxSource : ISource
    {
        private readonly Uri? uri;
        private readonly IArchiveHandler handler;

        public string SourceType => "Dropbox";
        public string FileName { get; }
        public long SourceFileSize { get; }
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        internal DropboxSource(Uri? uri, IArchiveHandler handler, string fileName, int fileSize)
        {
            this.uri = uri;
            this.handler = handler;
            FileName = fileName;
            SourceFileSize = fileSize;
        }

        public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            using var client = HttpClientFactory.Create();
            await using var stream = await client.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
            await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() { }
    }
}