using System.IO.Pipelines;
using System.Net.Http;
using System.Text.RegularExpressions;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using MediafireClient;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed partial class MediafireHandler : BaseSourceHandler
{
    //http://www.mediafire.com/file/tmybrjpmtrpcejl/DemonsSouls_CrashLog_Nov.19th.zip/file
    [GeneratedRegex(@"(?<mediafire_link>(https?://)?(www\.)?mediafire\.com/file/(?<quick_key>[^/\s]+)/(?<filename>[^/\?\s]+)(/file)?)", DefaultOptions)]
    private static partial Regex ExternalLink();
    private static readonly Client Client = new();

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
            if (m.Groups["mediafire_link"].Value is not { Length: > 0 } lnk
                || !Uri.TryCreate(lnk, UriKind.Absolute, out var webLink))
                continue;
            
            try
            {
                var filename = m.Groups["filename"].Value;
                var filesize = -1;

                Config.Log.Debug($"Trying to get download link for {webLink}…");
                var directLink = await Client.GetDirectDownloadLinkAsync(webLink, Config.Cts.Token).ConfigureAwait(false);
                if (directLink is null)
                    return Result.Failure<ISource>();

                Config.Log.Debug($"Trying to get content size for {directLink}…");
                using (var request = new HttpRequestMessage(HttpMethod.Head, directLink))
                {
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Config.Cts.Token);
                    if (response.Content.Headers.ContentLength > 0)
                        filesize = (int)response.Content.Headers.ContentLength.Value;
                    if (response.Content.Headers.ContentDisposition?.FileName is {Length: >0} fname)
                        filename = fname;
                }

                Config.Log.Debug($"Trying to get content stream for {directLink}…");
                await using var stream = await client.GetStreamAsync(directLink).ConfigureAwait(false);
                var buf = BufferPool.Rent(SnoopBufferSize);
                try
                {
                    var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                    foreach (var handler in handlers)
                    {
                        var result = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                        if (result.IsSuccess())
                            return Result.Success<ISource>(new MediafireSource(directLink, handler, filename, filesize));
                        else if (result.Message is {Length: >0})
                            return result.Cast<ISource>();
                    }
                    Config.Log.Debug("MediaFire Response:\n" + Encoding.UTF8.GetString(buf, 0, read));
                }
                finally
                {
                    BufferPool.Return(buf);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Error sniffing {m.Groups["mediafire_link"].Value}");
            }
        }
        return Result.Failure<ISource>();
    }

    private sealed class MediafireSource : ISource
    {
        private readonly Uri? uri;
        private readonly IArchiveHandler handler;

        public string SourceType => "Mediafire";
        public string FileName { get; }
        public long SourceFileSize { get; }
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        internal MediafireSource(Uri? uri, IArchiveHandler handler, string fileName, int fileSize)
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