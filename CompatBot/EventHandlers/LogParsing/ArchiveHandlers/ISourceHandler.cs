using System.IO.Pipelines;
using System.Threading.Tasks;

namespace CompatBot.EventHandlers.LogParsing.ArchiveHandlers
{
    internal interface IArchiveHandler
    {
        Task<bool> CanHandleAsync(string fileName, int fileSize, string url);
        Task FillPipeAsync(string url, PipeWriter writer);
    }
}