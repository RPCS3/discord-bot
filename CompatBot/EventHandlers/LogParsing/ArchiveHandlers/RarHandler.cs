using System;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Archives.Rar;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class RarHandler: IArchiveHandler
    {
        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            if (!fileName.EndsWith(".rar", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (fileSize > Config.AttachmentSizeLimit)
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
                                        read = await rarStream.ReadAsync(memory, Config.Cts.Token);
                                        writer.Advance(read);
                                        flushed = await writer.FlushAsync(Config.Cts.Token).ConfigureAwait(false);
                                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || Config.Cts.IsCancellationRequested));
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