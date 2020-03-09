using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using CompatBot.Utils;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class GenericLinkHandler : BaseSourceHandler
    {
        private static readonly Regex ExternalLink = new Regex(@"(?<link>(https?://)?(github\.com/RPCS3/rpcs3|cdn\.discordapp\.com/attachments)/.*/(?<filename>[^/\?\s]+\.(gz|zip|rar|7z|log)))", DefaultOptions);

        public override async Task<(ISource source, string failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return (null, null);

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return (null, null);

            using var client = HttpClientFactory.Create();
            foreach (Match m in matches)
            {
                if (m.Groups["link"].Value is string lnk
                    && !string.IsNullOrEmpty(lnk)
                    && Uri.TryCreate(lnk, UriKind.Absolute, out var uri)
                    && !"tty.log".Equals(m.Groups["filename"].Value, StringComparison.InvariantCultureIgnoreCase))
                {
                    try
                    {
                        var host = uri.Host;
                        var filename = Path.GetFileName(lnk);
                        var filesize = -1;

                        using (var request = new HttpRequestMessage(HttpMethod.Head, uri))
                        {
                            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Config.Cts.Token);
                            if (response?.Content?.Headers?.ContentLength > 0)
                                filesize = (int)response.Content.Headers.ContentLength.Value;
                            if (response?.Content?.Headers?.ContentDisposition?.FileNameStar is string fname && !string.IsNullOrEmpty(fname))
                                filename = fname;
                            uri = response.RequestMessage.RequestUri;
                        }

                        using var stream = await client.GetStreamAsync(uri).ConfigureAwait(false);
                        var buf = bufferPool.Rent(1024);
                        try
                        {
                            var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                            foreach (var handler in handlers)
                            {
                                var (canHandle, reason) = handler.CanHandle(filename, filesize, buf.AsSpan(0, read));
                                if (canHandle)
                                    return (new GenericSource(uri, handler, host, filename, filesize), null);
                                else if (!string.IsNullOrEmpty(reason))
                                    return (null, reason);
                            }
                        }
                        finally
                        {
                            bufferPool.Return(buf);
                        }
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, $"Error sniffing {m.Groups["link"].Value}");
                    }
                }
            }
            return (null, null);
        }

        private sealed class GenericSource : ISource
        {
            private readonly Uri uri;
            private readonly IArchiveHandler handler;

            public string SourceType => "Generic link";
            public string FileName { get; }
            public string Host { get; }
            public long SourceFileSize { get; }
            public long SourceFilePosition => handler.SourcePosition;
            public long LogFileSize => handler.LogSize;

            internal GenericSource(Uri uri, IArchiveHandler handler, string host, string fileName, int fileSize)
            {
                this.uri = uri;
                this.handler = handler;
                Host = host;
                FileName = fileName;
                SourceFileSize = fileSize;
            }

            public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
            {
                using var client = HttpClientFactory.Create();
                using var stream = await client.GetStreamAsync(uri).ConfigureAwait(false);
                await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
