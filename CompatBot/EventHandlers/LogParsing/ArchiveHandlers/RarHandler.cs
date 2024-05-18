using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;
using SharpCompress.Readers.Rar;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

internal sealed class RarHandler: IArchiveHandler
{
    private static readonly byte[] Header = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]; // Rar!..

    public long LogSize { get; private set; }
    public long SourcePosition { get; private set; }

    public (bool result, string? reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
    {
        if (header.Length >= Header.Length && header[..Header.Length].SequenceEqual(Header)
            || header.Length == 0 && fileName.EndsWith(".rar", StringComparison.InvariantCultureIgnoreCase))
        {
            var firstEntry = Encoding.ASCII.GetString(header);
            if (!firstEntry.Contains(".log", StringComparison.InvariantCultureIgnoreCase))
                return (false, "Archive doesn't contain any logs.");

            return (true, null);
        }

        return (false, null);
    }

    public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            await using var statsStream = new BufferCopyStream(sourceStream);
            using var rarReader = RarReader.Open(statsStream);
            while (rarReader.MoveToNextEntry())
            {
                if (!rarReader.Entry.IsDirectory
                    && rarReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                    && !rarReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                {
                    LogSize = rarReader.Entry.Size;
                    await using var rarStream = rarReader.OpenEntryStream();
                    int read;
                    FlushResult flushed;
                    do
                    {
                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                        read = await rarStream.ReadAsync(memory, cancellationToken);
                        writer.Advance(read);
                        SourcePosition = statsStream.Position;
                        flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                        SourcePosition = statsStream.Position;
                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
                    await writer.CompleteAsync();
                    return;
                }
                SourcePosition = statsStream.Position;
            }
            Config.Log.Warn("No rar entries that match the log criteria");
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Error filling the log pipe");
        }
        await writer.CompleteAsync();
    }
}