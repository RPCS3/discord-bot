using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using SharpCompress.Archives.SevenZip;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class SevenZipHandler: IArchiveHandler
    {
        private static readonly byte[] Header = {0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C};

        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public (bool result, string? reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            if (header.Length >= Header.Length && header.Slice(0, Header.Length).SequenceEqual(Header)
                || fileName.EndsWith(".7z", StringComparison.InvariantCultureIgnoreCase))
            {
                if (fileSize > Config.AttachmentSizeLimit)
                    return (false, $"Log size is too large: {fileSize.AsStorageUnit()} (max allowed is {Config.AttachmentSizeLimit.AsStorageUnit()})");

                return (true, null);
            }

            return (false, null);
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken)
        {
            try
            {
                await using var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);
                await sourceStream.CopyToAsync(fileStream, 16384, cancellationToken).ConfigureAwait(false);
                fileStream.Seek(0, SeekOrigin.Begin);
                using var zipArchive = SevenZipArchive.Open(fileStream);
                using var zipReader = zipArchive.ExtractAllEntries();
                while (zipReader.MoveToNextEntry())
                    if (!zipReader.Entry.IsDirectory
                        && zipReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                        && !zipReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                    {
                        LogSize = zipReader.Entry.Size;
                        await using var entryStream = zipReader.OpenEntryStream();
                        int read;
                        FlushResult flushed;
                        do
                        {
                            var memory = writer.GetMemory(Config.MinimumBufferSize);
                            read = await entryStream.ReadAsync(memory, cancellationToken);
                            writer.Advance(read);
                            flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                        } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
                        await writer.CompleteAsync();
                        return;
                    }
                Config.Log.Warn("No 7z entries that match the log criteria");
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Error filling the log pipe");
            }
            await writer.CompleteAsync();
        }
    }
}