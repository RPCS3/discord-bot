using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal sealed class PlainTextHandler: IArchiveHandler
    {
        public Task<bool> CanHandleAsync(string fileName, int fileSize, string url)
        {
            return Task.FromResult(fileName.EndsWith(".log", StringComparison.InvariantCultureIgnoreCase) && !fileName.Contains("tty.log", StringComparison.InvariantCultureIgnoreCase));
        }

        public async Task FillPipeAsync(Stream sourceStream, PipeWriter writer)
        {
            try
            {
                int read;
                FlushResult flushed;
                do
                {
                    var memory = writer.GetMemory(Config.MinimumBufferSize);
                    read = await sourceStream.ReadAsync(memory, Config.Cts.Token);
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
            writer.Complete();
        }
    }
}
