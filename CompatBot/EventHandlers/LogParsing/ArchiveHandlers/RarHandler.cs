using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using SharpCompress.Archives.Rar;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class RarHandler: IArchiveHandler
    {
        private static readonly byte[] Header = {0x52, 0x61, 0x72, 0x21, 0x1A, 0x07};

        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public (bool result, string reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            if (header.Length >= Header.Length && header.Slice(0, Header.Length).SequenceEqual(Header)
                || fileName.EndsWith(".rar", StringComparison.InvariantCultureIgnoreCase))
            {
                if (fileSize > Config.AttachmentSizeLimit)
                    return (false, $"Log size is too large: {fileSize.AsStorageUnit()} (max allowed is {Config.AttachmentSizeLimit.AsStorageUnit()})");

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
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    await sourceStream.CopyToAsync(fileStream, 16384, cancellationToken).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var rarArchive = RarArchive.Open(fileStream))
                    using (var rarReader = rarArchive.ExtractAllEntries())
                        while (rarReader.MoveToNextEntry())
                            if (!rarReader.Entry.IsDirectory
                                && rarReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                                && !rarReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                            {
                                LogSize = rarReader.Entry.Size;
                                using (var rarStream = rarReader.OpenEntryStream())
                                {
                                    int read;
                                    FlushResult flushed;
                                    do
                                    {
                                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                                        read = await rarStream.ReadAsync(memory, cancellationToken);
                                        writer.Advance(read);
                                        flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
                                }
                                writer.Complete();
                                return;
                            }
                    Config.Log.Warn("No rar entries that match the log criteria");
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Error filling the log pipe");
            }
            writer.Complete();
        }
    }
}