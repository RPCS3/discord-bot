using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal interface ISourceHandler
    {
        string FileName { get; }
        int FileSize { get; }

        Task<ISourceHandler> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers);
        Task FillPipeAsync(PipeWriter writer);
    }
}
