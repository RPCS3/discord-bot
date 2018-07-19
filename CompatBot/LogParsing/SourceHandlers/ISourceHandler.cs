﻿using System.IO.Pipelines;
using System.Threading.Tasks;
using DSharpPlus.Entities;

namespace CompatBot.LogParsing.SourceHandlers
{
    internal interface ISourceHandler
    {
        Task<bool> CanHandleAsync(DiscordAttachment attachment);
        Task FillPipeAsync(DiscordAttachment attachment, PipeWriter writer);
    }
}