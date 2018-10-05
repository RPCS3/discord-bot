using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    public class ZipHandler: ISourceHandler
    {
        private static readonly ArrayPool<byte> bufferPool = ArrayPool<byte>.Create(1024, 16);

        public async Task<bool> CanHandleAsync(DiscordAttachment attachment)
        {
            if (!attachment.FileName.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
                return false;

            try
            {
                using (var client = HttpClientFactory.Create())
                using (var stream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
                {
                    var buf = bufferPool.Rent(1024);
                    var read = await stream.ReadBytesAsync(buf).ConfigureAwait(false);
                    var firstEntry = Encoding.ASCII.GetString(new ReadOnlySpan<byte>(buf, 0, read));
                    var result = firstEntry.Contains(".log", StringComparison.InvariantCultureIgnoreCase);
                    bufferPool.Return(buf);
                    return result;
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Error sniffing the zip content");
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
                    using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
                    {
                        var logEntry = zipArchive.Entries.FirstOrDefault(e => e.Name.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase));
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