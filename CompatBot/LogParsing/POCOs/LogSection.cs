using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CompatBot.LogParsing
{
    internal class LogSection
    {
        public string EndTrigger;
        public Dictionary<string, Regex> Extractors;
        public Func<string, LogParseState, Task> OnNewLineAsync;
        public Action<LogParseState> OnSectionEnd;
    }
}