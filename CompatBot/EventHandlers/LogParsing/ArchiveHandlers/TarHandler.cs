using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

internal sealed class TarHandler: IArchiveHandler
{
    public static readonly TarHandler Instance = new();
    
    public long LogSize { get; private set; }
    public long SourcePosition { get; private set; }

    public Result CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
    {
        if (header.Length >= 9
            && Encoding.UTF8.GetString(header[..9]).Equals("RPCS3.log", StringComparison.OrdinalIgnoreCase))
            return Result.Success();
        return Result.Failure();
    }

    public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken)
    {
        await using var statsStream = new BufferCopyStream(sourceStream);
        await using var tarReader = new TarReader(statsStream);
        var entry = await tarReader.GetNextEntryAsync(true, cancellationToken).ConfigureAwait(false);
        if (entry is not { DataStream: { } dataStream })
            return;
        
        await using var stream = dataStream;
        try
        {
            int read;
            FlushResult flushed;
            do
            {
                var memory = writer.GetMemory(Config.MinimumBufferSize);
                read = await stream.ReadAsync(memory, cancellationToken);
                if (read > 0)
                    writer.Advance(read);
                SourcePosition = statsStream.Position;
                flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));

            var buf = statsStream.GetBufferedBytes();
            if (buf.Length > 3)
                LogSize = BitConverter.ToInt32(buf.AsSpan(buf.Length - 4, 4));
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