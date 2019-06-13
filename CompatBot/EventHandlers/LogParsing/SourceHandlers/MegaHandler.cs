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

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class MegaHandler : BaseSourceHandler
    {
        private static readonly Regex ExternalLink = new Regex(@"(?<mega_link>(https?://)?mega(\.co)?\.nz/#(?<mega_id>[^/>\s]+))", DefaultOptions);
        private static readonly IProgress<double> doodad = new Progress<double>(_ => { });

        public override async Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return null;

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return null;

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
                            var buf = bufferPool.Rent(1024);
                            int read;
                            try
                            {
                                using (var stream = await client.DownloadAsync(uri, doodad, Config.Cts.Token).ConfigureAwait(false))
                                    read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                                foreach (var handler in handlers)
                                    if (handler.CanHandle(node.Name, (int)node.Size, buf.AsSpan(0, read)))
                                        return new MegaSource(client, uri, node, handler);
                            }
                            finally
                            {
                                bufferPool.Return(buf);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Error sniffing {m.Groups["mega_link"].Value}");
                }
            }
            return null;
        }

        private sealed class MegaSource : ISource
        {
            private IMegaApiClient client;
            private Uri uri;
            private INodeInfo node;
            private IArchiveHandler handler;

            public string SourceType => "Mega";
            public string FileName => node.Name;
            public long SourceFileSize => node.Size;
            public long SourceFilePosition => handler.SourcePosition;
            public long LogFileSize => handler.LogSize;

            internal MegaSource(IMegaApiClient client, Uri uri, INodeInfo node, IArchiveHandler handler)
            {
                this.client = client;
                this.uri = uri;
                this.node = node;
                this.handler = handler;
            }

            public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
            {
                using (var stream = await client.DownloadAsync(uri, doodad, cancellationToken).ConfigureAwait(false))
                    await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
