using System;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class ZipHandler: IArchiveHandler
    {

        public bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            if (!fileName.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (fileSize > Config.LogSizeLimit)
                return false;

            var firstEntry = Encoding.ASCII.GetString(header);
            return firstEntry.Contains(".log", StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer)
        {
            try
            {
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    await sourceStream.CopyToAsync(fileStream, 16384, Config.Cts.Token).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        var logEntry = zipArchive.Entries.FirstOrDefault(e => e.Name.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase) && !e.Name.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase));
                        if (logEntry == null)
                            throw new InvalidOperationException("No zip entries that match the log criteria");

                        using (var zipStream = logEntry.Open())
                        {
                            int read;
                            FlushResult flushed;
                            do
                            {
                                var memory = writer.GetMemory(Config.MinimumBufferSize);
                                read = await zipStream.ReadAsync(memory, Config.Cts.Token);
                                writer.Advance(read);
                                flushed = await writer.FlushAsync(Config.Cts.Token).ConfigureAwait(false);
                            } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || Config.Cts.IsCancellationRequested));
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