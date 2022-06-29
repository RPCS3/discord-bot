using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using CG.Web.MegaApiClient;
using CompatBot.Utils;
using System.IO.Pipelines;
using System.Threading;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal sealed class MegaHandler : BaseSourceHandler
{
    // mega.nz/#!8IJHBYyB!jw21m-GCs85uzj9E5XRysqyJCsNfZS0Zx4Eu9_zvuUM
    // mega.nz/file/8IJHBYyB#jw21m-GCs85uzj9E5XRysqyJCsNfZS0Zx4Eu9_zvuUM
    private static readonly Regex ExternalLink = new(@"(?<mega_link>(https?://)?mega(\.co)?\.nz/(#(?<mega_id>[^/>\s]+)|file/(?<new_mega_id>[^/>\s]+)))", DefaultOptions);
    private static readonly IProgress<double> Doodad = new Progress<double>(_ => { });

    public override async Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
    {
        if (string.IsNullOrEmpty(message.Content))
            return (null, null);

        var matches = ExternalLink.Matches(message.Content);
        if (matches.Count == 0)
            return (null, null);

        var client = new MegaApiClient();
        await client.LoginAnonymousAsync();
        foreach (Match m in matches)
        {
            try
            {
                if (m.Groups["mega_link"].Value is string lnk
                    && !string.IsNullOrEmpty(lnk)
                    && Uri.TryCreate(lnk, UriKind.Absolute, out var uri))
                {
                    var node = await client.GetNodeFromLinkAsync(uri).ConfigureAwait(false);
                    if (node.Type == NodeType.File)
                    {
                        var buf = BufferPool.Rent(SnoopBufferSize);
                        try
                        {
                            int read;
                            await using (var stream = await client.DownloadAsync(uri, Doodad, Config.Cts.Token).ConfigureAwait(false))
                                read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                            foreach (var handler in handlers)
                            {
                                var (canHandle, reason) = handler.CanHandle(node.Name, (int)node.Size, buf.AsSpan(0, read));
                                if (canHandle)
                                    return (new MegaSource(client, uri, node, handler), null);
                                else if (!string.IsNullOrEmpty(reason))
                                    return (null, reason);
                            }
                        }
                        finally
                        {
                            BufferPool.Return(buf);
                        }
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

    private sealed class MegaSource : ISource
    {
        private readonly IMegaApiClient client;
        private readonly Uri uri;
        private readonly INode node;
        private readonly IArchiveHandler handler;

        public string SourceType => "Mega";
        public string FileName => node.Name;
        public long SourceFileSize => node.Size;
        public long SourceFilePosition => handler.SourcePosition;
        public long LogFileSize => handler.LogSize;

        internal MegaSource(IMegaApiClient client, Uri uri, INode node, IArchiveHandler handler)
        {
            this.client = client;
            this.uri = uri;
            this.node = node;
            this.handler = handler;
        }

        public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            await using var stream = await client.DownloadAsync(uri, Doodad, cancellationToken).ConfigureAwait(false);
            await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
        }
    }
}