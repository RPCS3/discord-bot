using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;
using CompatBot.Utils;
using System.IO.Pipelines;
using System.Net.Http;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal sealed class PastebinHandler : BaseSourceHandler
    {
        private static readonly Regex ExternalLink = new Regex(@"(?<pastebin_link>(https?://)pastebin.com/(raw/)?(?<pastebin_id>[^/>\s]+))", DefaultOptions);

        public override async Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers)
        {
            if (string.IsNullOrEmpty(message.Content))
                return null;

            var matches = ExternalLink.Matches(message.Content);
            if (matches.Count == 0)
                return null;

            foreach (Match m in matches)
            {
                try
                {
                    if (m.Groups["pastebin_id"].Value is string pid
                        && !string.IsNullOrEmpty(pid))
                    {
                        var uri = new Uri("https://pastebin.com/raw/" + pid);
                        using (var client = HttpClientFactory.Create())
                        using (var stream = await client.GetStreamAsync(uri).ConfigureAwait(false))
                        {
                            var buf = bufferPool.Rent(1024);
                            try
                            {
                                var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                                var filename = pid + ".log";
                                var filesize = stream.CanSeek ? (int)stream.Length : 0;
                                foreach (var handler in handlers)
                                    if (handler.CanHandle(filename, filesize, buf.AsSpan(0, read)))
                                        return new PastebinSource(uri, filename, filesize, handler);
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

        private sealed class PastebinSource : ISource
        {
            private Uri uri;
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

            public async Task FillPipeAsync(PipeWriter writer)
            {
                using (var client = HttpClientFactory.Create())
                using (var stream = await client.GetStreamAsync(uri).ConfigureAwait(false))
                    await handler.FillPipeAsync(stream, writer).ConfigureAwait(false);
            }
        }
    }
}
