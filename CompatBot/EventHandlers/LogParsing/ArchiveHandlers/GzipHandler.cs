using System;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class GzipHandler: IArchiveHandler
    {
        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            return fileName.EndsWith(".log.gz", StringComparison.InvariantCultureIgnoreCase)
                   && !fileName.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken)
        {
            using (var statsStream = new BufferCopyStream(sourceStream) )
            using (var gzipStream = new GZipStream(statsStream, CompressionMode.Decompress))
            {
                try
                {
                    int read;
                    FlushResult flushed;
                    do
                    {
                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                        read = await gzipStream.ReadAsync(memory, cancellationToken);
                        writer.Advance(read);
                        SourcePosition = statsStream.Position;
                        flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                        SourcePosition = statsStream.Position;
                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));

                    var buf = statsStream.GetBufferedBytes();
                    if (buf.Length > 3)
                        LogSize = BitConverter.ToInt32(buf.AsSpan(buf.Length - 4, 4));
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Error filling the log pipe");
                    writer.Complete(e);
                    return;
                }
            }
            writer.Complete();
        }
    }
}