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
using YandexDiskClient;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed partial class YandexDiskHandler: BaseSourceHandler
{
    [GeneratedRegex(@"(?<yadisk_link>(https?://)?(www\.)?yadi\.sk/d/(?<share_key>[^/>\s]+))\b", DefaultOptions)]
    private static partial Regex ExternalLink();
    private static readonly Client Client = new();

    public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
    {
        if (string.IsNullOrEmpty(message.Content))
            return (null, null);

        var matches = ExternalLink().Matches(message.Content);
        if (matches.Count == 0)
            return (null, null);

        using var client = HttpClientFactory.Create();
        foreach (Match m in matches)
        {
            if (m.Groups["yadisk_link"].Value is not { Length: > 0 } lnk
                || !Uri.TryCreate(lnk, UriKind.Absolute, out var webLink))
                continue;
            
            try
            {
                var filename = "";
                var filesize = -1;

                var resourceInfo = await Client.GetResourceInfoAsync(webLink, Config.Cts.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(resourceInfo?.File))
                    return (null, null);

                if (resourceInfo.Size.HasValue)
                    filesize = resourceInfo.Size.Value;
                if (!string.IsNullOrEmpty(resourceInfo.Name))
                    filename = resourceInfo.Name;

                await using var stream = await client.GetStreamAsync(resourceInfo.File).ConfigureAwait(false);
                var buf = BufferPool.Rent(SnoopBufferSize);
                try
                {
                    var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                    foreach (var handler in handlers)
                    {
                        var (canHandle, reason) = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                        if (canHandle)
                            return (new YaDiskSource(resourceInfo.File, handler, filename, filesize), null);
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
                Config.Log.Warn(e, $"Error sniffing {m.Groups["yadisk_link"].Value}");
            }
        }
        return (null, null);
    }

    private sealed class YaDiskSource : ISource
    {
        private readonly Uri uri;
        private readonly IArchiveHandler handler;

        public string SourceType => "Ya.Disk";
        public string FileName { get; }
        public long SourceFileSize { get; }
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        internal YaDiskSource(string uri, IArchiveHandler handler, string fileName, int fileSize)
        {
            this.uri = new Uri(uri);
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