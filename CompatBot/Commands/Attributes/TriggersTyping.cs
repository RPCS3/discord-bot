using System;
using DSharpPlus.CommandsNext;

namespace CompatBot.Commands.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
internal class TriggersTyping: Attribute
{
    public bool InDmOnly { get; set; }

    public bool ExecuteCheck(CommandContext ctx)
        => !InDmOnly || ctx.Channel.IsPrivate;
}