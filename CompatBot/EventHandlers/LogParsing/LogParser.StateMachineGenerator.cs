﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.POCOs;
using NReco.Text;

namespace CompatBot.EventHandlers.LogParsing
{
    using SectionAction = KeyValuePair<string, Action<string, LogParseState>>;

    internal partial class LogParser
    {
        private static readonly ReadOnlyCollection<LogSectionParser> SectionParsers;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        static LogParser()
        {
            var parsers = new List<LogSectionParser>(LogSections.Count);
            foreach (var sectionDescription in LogSections)
            {
                var parser = new LogSectionParser
                {
                    OnLineCheckAsync = sectionDescription.OnNewLineAsync ?? ((l, s) => Task.CompletedTask),
                    OnSectionEnd = sectionDescription.OnSectionEnd,
                    EndTrigger = Encoding.ASCII.GetString(Utf8.GetBytes(sectionDescription.EndTrigger)),
                };
                // the idea here is to construct Aho-Corasick parser that will look for any data marker and run the associated regex to extract the data into state
                if (sectionDescription.Extractors?.Count > 0)
                {
                    var act = new AhoCorasickDoubleArrayTrie<Action<string, LogParseState>>(sectionDescription.Extractors.Select(extractorPair =>
                        new SectionAction(
                            Encoding.ASCII.GetString(Utf8.GetBytes(extractorPair.Key)),
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
#if DEBUG
                    Console.WriteLine($"regex {group.Name} = {group.Value}");
#endif
                    state.WipCollection[group.Name] = Utf8.GetString(Encoding.ASCII.GetBytes(group.Value));
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