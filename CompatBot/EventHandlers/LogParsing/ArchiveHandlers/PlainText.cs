using System;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    public class PlainTextHandler: IArchiveHandler
    {
        public Task<bool> CanHandleAsync(string fileName, int fileSize, string url)
        {
            return Task.FromResult(fileName.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase));
        }

        public async Task FillPipeAsync(string url, PipeWriter writer)
        {
            using (var client = HttpClientFactory.Create())
            using (var stream = await client.GetStreamAsync(url).ConfigureAwait(false))
            {
                try
                {
                    int read;
                    FlushResult flushed;
                    do
                    {
                        var memory = writer.GetMemory(Config.MinimumBufferSize);
                        read = await stream.ReadAsync(memory, Config.Cts.Token);
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
