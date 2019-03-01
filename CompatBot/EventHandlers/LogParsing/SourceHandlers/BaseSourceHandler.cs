using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers
{
    internal abstract class BaseSourceHandler: ISourceHandler
    {
        protected static readonly ArrayPool<byte> bufferPool = ArrayPool<byte>.Create(1024, 64);

        public abstract Task<ISource> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers);
    }
}
