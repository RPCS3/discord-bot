using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class SevenZipHandler: IArchiveHandler
    {
        public long LogSize { get; private set; }
        public long SourcePosition { get; private set; }

        public bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header)
        {
            if (!fileName.EndsWith(".7z", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (fileSize > Config.AttachmentSizeLimit)
                return false;

            return true;
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer)
        {
            try
            {
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    await sourceStream.CopyToAsync(fileStream, 16384, Config.Cts.Token).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var zipArchive = SevenZipArchive.Open(fileStream))
                    using (var zipReader = zipArchive.ExtractAllEntries())
                        while (zipReader.MoveToNextEntry())
                            if (!zipReader.Entry.IsDirectory
                                && zipReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                                && !zipReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                            {
                                LogSize = zipReader.Entry.Size;
                                using (var entryStream = zipReader.OpenEntryStream())
                                {
                                    int read;
                                    FlushResult flushed;
                                    do
                                    {
                                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                                        read = await entryStream.ReadAsync(memory, Config.Cts.Token);
                                        writer.Advance(read);
                                        flushed = await writer.FlushAsync(Config.Cts.Token).ConfigureAwait(false);
                                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || Config.Cts.IsCancellationRequested));
                                }
                                writer.Complete();
                                return;
                            }
                    Config.Log.Warn("No 7z entries that match the log criteria");
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