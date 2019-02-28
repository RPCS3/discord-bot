using System;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    public class GzipHandler: IArchiveHandler
    {
        public Task<bool> CanHandleAsync(string fileName, int fileSize, string url)
        {
            return Task.FromResult(fileName.EndsWith(".log.gz", StringComparison.InvariantCultureIgnoreCase)
                                   && !fileName.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase));
        }

        public async Task FillPipeAsync(string url, PipeWriter writer)
        {
            using (var client = HttpClientFactory.Create())
            using (var downloadStream = await client.GetStreamAsync(url).ConfigureAwait(false))
            using (var gzipStream = new GZipStream(downloadStream, CompressionMode.Decompress))
            {
                try
                {
                    int read;
                    FlushResult flushed;
                    do
                    {
                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                        read = await gzipStream.ReadAsync(memory, Config.Cts.Token);
                        writer.Advance(read);
                        flushed = await writer.FlushAsync(Config.Cts.Token).ConfigureAwait(false);
                    } while (read > 0 && !(flushed.IsCompleted || flushed.IsCanceled || Config.Cts.IsCancellationRequested));
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Error filling the log pipe");
                    writer.Complete(e);
                    return;
                }
            }
            writer.Complete();
        }
    }
}