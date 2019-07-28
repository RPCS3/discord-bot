using System;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class ZipHandler: IArchiveHandler
    {
        private static readonly byte[] Header = { 0x50, 0x4B, 0x03, 0x04 };

        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public (bool result, string reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {

            if (header.Length >= Header.Length && header.Slice(0, Header.Length).SequenceEqual(Header)
                || fileName.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
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
                    using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        var logEntry = zipArchive.Entries.FirstOrDefault(e => e.Name.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase) && !e.Name.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase));
                        if (logEntry == null)
                            throw new InvalidOperationException("No zip entries that match the log criteria");

                        LogSize = logEntry.Length;
                        using (var zipStream = logEntry.Open())
                        {
                            int read;
                            FlushResult flushed;
                            do
                            {
                                var memory = writer.GetMemory(Config.MinimumBufferSize);
                                read = await zipStream.ReadAsync(memory, cancellationToken);
                                writer.Advance(read);
                                flushed = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                            } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || cancellationToken.IsCancellationRequested));
                        }
                    }
                }
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