using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.Utils;
using NReco.Text;

namespace CompatBot.EventHandlers.LogParsing
{
    using SectionAction = KeyValuePair<string, Action<string, LogParseState>>;

    internal partial class LogParser
    {
        private static readonly ReadOnlyCollection<LogSectionParser> SectionParsers;

        static LogParser()
        {
            var parsers = new List<LogSectionParser>(LogSections.Count);
            foreach (var sectionDescription in LogSections)
            {
                var parser = new LogSectionParser
                {
                    OnLineCheckAsync = sectionDescription.OnNewLineAsync ?? ((l, s) => Task.CompletedTask),
                    OnSectionEnd = sectionDescription.OnSectionEnd,
                    EndTrigger = sectionDescription.EndTrigger.ToLatin8BitEncoding(),
                };
                // the idea here is to construct Aho-Corasick parser that will look for any data marker and run the associated regex to extract the data into state
                if (sectionDescription.Extractors?.Count > 0)
                {
                    var act = new AhoCorasickDoubleArrayTrie<Action<string, LogParseState>>(sectionDescription.Extractors.Select(extractorPair =>
                        new SectionAction(
                            extractorPair.Key.ToLatin8BitEncoding(),
                            (buffer, state) => OnExtractorHit(buffer, extractorPair.Value, state)
                        )
                    ), true);
                    parser.OnExtract = (line, buffer, state) => { act.ParseText(line, h => { h.Value(buffer, state); }); };
                }
                parsers.Add(parser);
            }
            SectionParsers = parsers.AsReadOnly();
        }

        private static void OnExtractorHit(string buffer, Regex extractor, LogParseState state)
        {
            var matches = extractor.Matches(buffer);
            foreach (Match match in matches)
            foreach (Group group in match.Groups)
                if (!string.IsNullOrEmpty(group.Name) && group.Name != "0" && !string.IsNullOrWhiteSpace(group.Value))
                {
                    Config.Log.Debug($"regex {group.Name} = {group.Value}");
                    if (MultiValueItems.Contains(group.Name))
                    {
                        var currentValue = state.WipCollection[group.Name];
                        if (!string.IsNullOrEmpty(currentValue))
                            currentValue += Environment.NewLine;
                        state.WipCollection[group.Name] = currentValue + group.Value.ToUtf8();
                    }
                    else
                        state.WipCollection[group.Name] = group.Value.ToUtf8();
                }
        }

        private delegate void OnNewLineDelegate(string line, string buffer, LogParseState state);

        private class LogSectionParser
        {
            public OnNewLineDelegate OnExtract;
            public Func<string, LogParseState, Task> OnLineCheckAsync;
            public Action<LogParseState> OnSectionEnd;
            public string EndTrigger;
        }
    }
}