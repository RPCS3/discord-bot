using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class PlainTextHandler: IArchiveHandler
    {
        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            LogSize = fileSize;
            return fileName.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                   && !fileName.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase)
                   && header.Length > 8
                   && Encoding.UTF8.GetString(header.Slice(0, 8)).Contains("RPCS3");
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                int read;
                FlushResult flushed;
                do
                {
                    var memory = writer.GetMemory(Config.MinimumBufferSize);
                    read = await sourceStream.ReadAsync(memory, cancellationToken);
                    writer.Advance(read);
                    flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Error filling the log pipe");
                writer.Complete(e);
                return;
            }
            writer.Complete();
        }
    }
}
