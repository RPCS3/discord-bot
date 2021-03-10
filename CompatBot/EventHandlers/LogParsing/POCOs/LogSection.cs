using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CompatBot.Utils;

namespace CompatBot.EventHandlers.LogParsing.POCOs
{
    internal class LogSection
    {
        public string[] EndTrigger = null!;

        public Dictionary<string, Regex> Extractors
        {
            get => extractors;
            init
            {
                var result = new Dictionary<string, Regex>(value.Count);
                foreach (var key in value.Keys)
                {
                    var r = value[key];
                    result[key] = new(r.ToLatin8BitRegexPattern(), r.Options);
                }
                extractors = result;
            }
        }

        public Action<LogParseState>? OnSectionEnd;
        private readonly Dictionary<string, Regex> extractors = null!;
    }
}