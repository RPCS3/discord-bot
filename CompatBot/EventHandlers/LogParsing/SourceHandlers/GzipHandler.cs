using System;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    public class GzipHandler: ISourceHandler
    {
        public Task<bool> CanHandleAsync(DiscordAttachment attachment)
        {
            return Task.FromResult(attachment.FileName.EndsWith(".log.gz", StringComparison.InvariantCultureIgnoreCase));
        }

        public async Task FillPipeAsync(DiscordAttachment attachment, PipeWriter writer)
        {
            using (var client = HttpClientFactory.Create())
            using (var downloadStream = await client.GetStreamAsync(attachment.Url).ConfigureAwait(false))
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
                    Console.WriteLine(e);
                    writer.Complete(e);
                    return;
                }
            }
            writer.Complete();
        }
    }
}