using System.IO;
using System.IO.Pipelines;
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
                    await using var zipStream = zipReader.OpenEntryStream();
                    int read, totalRead = 0;
                    FlushResult flushed;
                    do
                    {
                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                        read = await zipStream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
#if DEBUG
                        Config.Log.Debug($"{nameof(ZipHandler)}: read {read} bytes from source stream");
#endif
                        if (read > 0)
                            writer.Advance(read);
                        totalRead += read;
#if DEBUG
                        Config.Log.Debug($"{nameof(ZipHandler)}: advanced the writer by {read} (total read {totalRead})");
#endif
                        SourcePosition = statsStream.Position;
#if DEBUG
                        Config.Log.Debug($"{nameof(ZipHandler)}: current source position is {SourcePosition}");
#endif
                        flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
#if DEBUG
                        Config.Log.Debug($"{nameof(ZipHandler)}: flushed the writer");
#endif
                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
                    await writer.CompleteAsync().ConfigureAwait(false);
#if DEBUG
                    Config.Log.Debug($"{nameof(ZipHandler)}: writer completed");
#endif
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