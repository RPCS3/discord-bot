using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;
using SharpCompress.Readers.Zip;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

internal sealed class ZipHandler: IArchiveHandler
{
    private static readonly byte[] Header = [0x50, 0x4B, 0x03, 0x04]; //PK..

    public long LogSize { get; private set; }
    public long SourcePosition { get; private set; }

    public (bool result, string? reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
    {

        if (header.Length >= Header.Length && header[..Header.Length].SequenceEqual(Header)
            || header.Length == 0 && fileName.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
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
            using var zipReader = ZipReader.Open(statsStream);
            while (zipReader.MoveToNextEntry())
            {
                if (!zipReader.Entry.IsDirectory
                    && zipReader.Entry.Key!.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                    && !zipReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                {
                    LogSize = zipReader.Entry.Size;
                    await using var rarStream = zipReader.OpenEntryStream();
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