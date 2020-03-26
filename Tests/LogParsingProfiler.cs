using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class LogParsingProfiler
    {
        private static readonly IArchiveHandler[] archiveHandlers =
        {
            new GzipHandler(),
            new ZipHandler(),
            new RarHandler(),
            new SevenZipHandler(),
            new PlainTextHandler(),
        };

        [Explicit("For performance profiling only")]
        [TestCase(@"C:\Documents\Downloads\RPCS3(206)_perf_problem.log")]
        public async Task Analyze(string path)
        {
            var cts = new CancellationTokenSource();
            var source = await FileSource.DetectArchiveHandlerAsync(path, archiveHandlers).ConfigureAwait(false);
            var pipe = new Pipe();
            var fillPipeTask = source.FillPipeAsync(pipe.Writer, cts.Token);
            var readPipeTask = LogParser.ReadPipeAsync(pipe.Reader, cts.Token);
            var result = await readPipeTask.ConfigureAwait(false);
            await fillPipeTask.ConfigureAwait(false);
            result.TotalBytes = source.LogFileSize;
            Assert.That(result.CompleteCollection, Is.Not.Null.And.Not.Empty);
        }
    }
}
