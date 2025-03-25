using System.IO.Pipelines;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

public interface ISourceHandler
{
    Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers);
}

public interface ISource: IDisposable
{
    string SourceType { get; }
    string FileName { get; }
    long SourceFileSize { get; }
    long SourceFilePosition { get; }
    long LogFileSize { get; }
    Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken);
}