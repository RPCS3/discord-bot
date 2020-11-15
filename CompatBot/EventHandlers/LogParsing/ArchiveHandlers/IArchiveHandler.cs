using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    public interface IArchiveHandler
    {
        (bool result, string? reason) CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header);
        Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken);
        long LogSize { get; }
        long SourcePosition { get; }
    }
}