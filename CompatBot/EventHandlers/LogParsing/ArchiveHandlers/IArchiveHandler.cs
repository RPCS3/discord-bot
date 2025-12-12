using System.IO;
using System.IO.Pipelines;
using ResultNet;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

public interface IArchiveHandler
{
    Result CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header);
    Task FillPipeAsync(Stream sourceStream, PipeWriter writer, CancellationToken cancellationToken);
    long LogSize { get; }
    long SourcePosition { get; }
}