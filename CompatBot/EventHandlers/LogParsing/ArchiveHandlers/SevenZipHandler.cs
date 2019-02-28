using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using SharpCompress.Archives.SevenZip;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    public class SevenZipHandler: IArchiveHandler
    {
        private static readonly ArrayPool<byte> bufferPool = ArrayPool<byte>.Create(1024, 16);

        public Task<bool> CanHandleAsync(string fileName, int fileSize, string url)
        {
            if (!fileName.EndsWith(".7z", StringComparison.InvariantCultureIgnoreCase))
                return Task.FromResult(false);

            if (fileSize > Config.AttachmentSizeLimit)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        public async Task FillPipeAsync(string url, PipeWriter writer)
        {
            try
            {
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    using (var client = HttpClientFactory.Create())
                    using (var downloadStream = await client.GetStreamAsync(url).ConfigureAwait(false))
                        await downloadStream.CopyToAsync(fileStream, 16384, Config.Cts.Token).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var zipArchive = SevenZipArchive.Open(fileStream))
                    using (var zipReader = zipArchive.ExtractAllEntries())
                        while (zipReader.MoveToNextEntry())
                            if (!zipReader.Entry.IsDirectory
                                && zipReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase)
                                && !zipReader.Entry.Key.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase))
                            {
                                using (var rarStream = zipReader.OpenEntryStream())
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