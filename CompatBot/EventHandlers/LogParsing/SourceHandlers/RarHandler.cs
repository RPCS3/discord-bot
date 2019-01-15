using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;
using SharpCompress.Archives.Rar;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    public class RarHandler: ISourceHandler
    {
        private static readonly ArrayPool<byte> bufferPool = ArrayPool<byte>.Create(1024, 16);

        public async Task<bool> CanHandleAsync(DiscordAttachment attachment)
        {
            if (!attachment.FileName.EndsWith(".rar", StringComparison.InvariantCultureIgnoreCase))
                return false;

            if (attachment.FileSize > Config.AttachmentSizeLimit)
                return false;

            try
            {
                using (var client = HttpClientFactory.Create())
                using (var stream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
                {
                    var buf = bufferPool.Rent(1024);
                    bool result;
                    try
                    {
                        var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                        var firstEntry = Encoding.ASCII.GetString(new ReadOnlySpan<byte>(buf, 0, read));
                        result = firstEntry.Contains(".log", StringComparison.InvariantCultureIgnoreCase);
                    }
                    finally
                    {
                        bufferPool.Return(buf);
                    }
                    return result;
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Error sniffing the rar content");
                return false;
            }
        }

        public async Task FillPipeAsync(DiscordAttachment attachment, PipeWriter writer)
        {
            try
            {
                using (var fileStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16384, FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose))
                {
                    using (var client = HttpClientFactory.Create())
                    using (var downloadStream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
                        await downloadStream.CopyToAsync(fileStream, 16384, Config.Cts.Token).ConfigureAwait(false);
                    fileStream.Seek(0, SeekOrigin.Begin);
                    using (var rarArchive = RarArchive.Open(fileStream))
                    using (var rarReader = rarArchive.ExtractAllEntries())
                        while (rarReader.MoveToNextEntry())
                            if (!rarReader.Entry.IsDirectory && rarReader.Entry.Key.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase))
                            {
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