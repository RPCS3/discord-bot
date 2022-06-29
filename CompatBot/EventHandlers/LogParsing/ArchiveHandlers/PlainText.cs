using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

internal sealed class PlainTextHandler: IArchiveHandler
{
    public long LogSize { get; private set; }
    public long SourcePosition { get; private set; }

    public (bool result, string? reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
    {
        LogSize = fileSize;
        if (fileName.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
            return (false, null);

        if (header.Length > 10 && Encoding.UTF8.GetString(header.Slice(0, 30)).Contains("RPCS3 v"))
            return (true, null);

        return (false, null);
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
            await writer.CompleteAsync(e);
            return;
        }
        await writer.CompleteAsync();
    }
}