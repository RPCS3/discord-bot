using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal interface ISourceHandler
    {
        Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers);
    }

    internal interface ISource
    {
        string SourceType { get; }
        string FileName { get; }
        int FileSize { get; }
        Task FillPipeAsync(PipeWriter writer);
    }
}
