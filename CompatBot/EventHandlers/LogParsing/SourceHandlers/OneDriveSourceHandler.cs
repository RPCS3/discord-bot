using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.Utils;
using DSharpPlus.Entities;
using OneDriveClient;
using OneDriveClient.POCOs;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class OneDriveSourceHandler : BaseSourceHandler
    {
        private static readonly Regex ExternalLink = new(@"(?<onedrive_link>(https?://)?(1drv\.ms|onedrive\.live\.com)/[^>\s]+)", DefaultOptions);
        private static readonly Client Client = new();

        public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return (null, null);

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return (null, null);

            using var httpClient = HttpClientFactory.Create();
            foreach (Match m in matches)
            {
                try
                {
                    if (m.Groups["onedrive_link"].Value is string lnk
                        && !string.IsNullOrEmpty(lnk)
                        && Uri.TryCreate(lnk, UriKind.Absolute, out var uri)
                        && await Client.ResolveContentLinkAsync(uri, Config.Cts.Token).ConfigureAwait(false) is DriveItemMeta itemMeta
                        && itemMeta.ContentDownloadUrl is string downloadUrl)
                    {
                        try
                        {
                            var filename = itemMeta.Name ?? "";
                            var filesize = itemMeta.Size;
                            uri = new Uri(downloadUrl);

                            await using var stream = await httpClient.GetStreamAsync(uri).ConfigureAwait(false);
                            var buf = BufferPool.Rent(SnoopBufferSize);
                            try
                            {
                                var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                                foreach (var handler in handlers)
                                {
                                    var (canHandle, reason) = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                                    if (canHandle)
                                        return (new OneDriveSource(uri, handler, filename, filesize), null);
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
                            Config.Log.Warn(e, $"Error sniffing {m.Groups["link"].Value}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Error sniffing {m.Groups["mega_link"].Value}");
                }
            }
            return (null, null);
        }


        private sealed class OneDriveSource : ISource
        {
            private readonly Uri uri;
            private readonly IArchiveHandler handler;

            public string SourceType => "OneDrive";
            public string FileName { get; }
            public long SourceFileSize { get; }
            public long SourceFilePosition => handler.SourcePosition;
            public long LogFileSize => handler.LogSize;

            internal OneDriveSource(Uri uri, IArchiveHandler handler, string fileName, int fileSize)
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
        }
    }
}