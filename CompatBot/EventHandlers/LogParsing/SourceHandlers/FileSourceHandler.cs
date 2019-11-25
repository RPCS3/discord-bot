using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.Utils;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal class FileSource : ISource
    {
        private readonly string path;
        private readonly IArchiveHandler handler;

        public FileSource(string path, IArchiveHandler handler)
        {
            this.path = path;
            this.handler = handler;
            var fileInfo = new FileInfo(path);
            SourceFileSize = fileInfo.Length;
            FileName = fileInfo.Name;
        }

        public string SourceType => "File";
        public string FileName { get; }
        public long SourceFileSize { get; }
        public long SourceFilePosition { get; }
        public long LogFileSize { get; }

        public async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            await handler.FillPipeAsync(stream, writer, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<ISource> DetectArchiveHandlerAsync(string path, ICollection<IArchiveHandler> handlers)
        {
            var buf = new byte[1024];
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
            foreach (var handler in handlers)
            {
                var (canHandle, reason) = handler.CanHandle(Path.GetFileName(path), (int)stream.Length, buf.AsSpan(0, read));
                if (canHandle)
                    return new FileSource(path, handler);
                
                if (!string.IsNullOrEmpty(reason))
                    throw new InvalidOperationException(reason);
            }
            throw new InvalidOperationException("Unknown source type");
        }
    }
}