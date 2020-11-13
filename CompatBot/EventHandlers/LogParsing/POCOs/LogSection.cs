using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CompatBot.EventHandlers.LogParsing.POCOs
{
    internal class LogSection
    {
        public string[] EndTrigger = null!;
        public Dictionary<string, Regex> Extractors = null!;
        public Action<LogParseState>? OnSectionEnd;
    }
}