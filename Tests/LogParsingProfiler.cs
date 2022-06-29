using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot;
using CompatBot.EventHandlers.LogParsing;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class LogParsingProfiler
{
    private static readonly IArchiveHandler[] ArchiveHandlers =
    {
        new GzipHandler(),
        new ZipHandler(),
        new RarHandler(),
        new SevenZipHandler(),
        new PlainTextHandler(),
    };

    [Explicit("For performance profiling only")]
    [TestCase(@"C:\Documents\Downloads\RPCS3_20.log", TestName = "Plaintext")]
    [TestCase(@"C:\Documents\Downloads\RPCS3_20.log.gz", TestName = "gz")]
    [TestCase(@"C:\Documents\Downloads\RPCS3_20.zip", TestName = "zip")]
    [TestCase(@"C:\Documents\Downloads\RPCS3_20.rar", TestName = "rar")]
    [TestCase(@"C:\Documents\Downloads\RPCS3_20.7z", TestName = "7z")]
    public async Task PerformanceTest(string path)
    {
        Config.InMemorySettings[nameof(Config.AttachmentSizeLimit)] = int.MaxValue.ToString();
        Config.RebuildConfiguration();
        var timer = Stopwatch.StartNew();
        var cts = new CancellationTokenSource();
        var source = await FileSource.DetectArchiveHandlerAsync(path, ArchiveHandlers).ConfigureAwait(false);
        var pipe = new Pipe();
        var fillPipeTask = source.FillPipeAsync(pipe.Writer, cts.Token);
        var readPipeTask = LogParser.ReadPipeAsync(pipe.Reader, cts.Token);
        var result = await readPipeTask.ConfigureAwait(false);
        await fillPipeTask.ConfigureAwait(false);
        timer.Stop();
        Config.Log.Info($"Total time {Path.GetExtension(path)}: {timer.Elapsed.TotalSeconds}s");
        result.TotalBytes = source.LogFileSize;
#if DEBUG
        Config.Log.Debug("~~~~~~~~~~~~~~~~~~~~");
        Config.Log.Debug("Extractor hit stats (CPU time, s / total hits):");
        foreach (var (key, (count, time)) in result.ExtractorHitStats.OrderByDescending(kvp => kvp.Value.regexTime))
        {
            var ttime = TimeSpan.FromTicks(time).TotalSeconds;
            var msg = $"{ttime:0.000}/{count} ({ttime / count:0.000000}): {key}";
            if (count > 100000 || ttime > 20)
                Config.Log.Fatal(msg);
            else if (count > 10000 || ttime > 10)
                Config.Log.Error(msg);
            else if (count > 1000 || ttime > 5)
                Config.Log.Warn(msg);
            else if (count > 100 || ttime > 1)
                Config.Log.Info(msg);
            else
                Config.Log.Debug(msg);
        }
        Config.Log.Debug("~~~~~~~~~~~~~~~~~~~~");
#endif
        Assert.That(result.CompletedCollection, Is.Not.Null.And.Not.Empty);
    }
}