using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal interface IArchiveHandler
    {
        bool CanHandle(string fileName, int fileSize, ReadOnlySpan<byte> header);
        Task FillPipeAsync(Stream sourceStream, PipeWriter writer);
        long LogSize { get; }
    }
}