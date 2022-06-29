using System.Buffers;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using DSharpPlus.Entities;

namespace CompatBot.EventHandlers.LogParsing.SourceHandlers;

internal abstract class BaseSourceHandler: ISourceHandler
{
    protected const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture;
    protected const int SnoopBufferSize = 4096;
    internal static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Create(SnoopBufferSize, 64);

    public abstract Task<(ISource? source, string? failReason)> FindHandlerAsync(DiscordMessage message, ICollection<IArchiveHandler> handlers);
}