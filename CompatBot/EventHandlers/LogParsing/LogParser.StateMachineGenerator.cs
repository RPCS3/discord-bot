using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
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
                    OnSectionEnd = sectionDescription.OnSectionEnd,
                    EndTrigger = sectionDescription.EndTrigger.Select(s => s.ToLatin8BitEncoding()).ToArray(),
                };
                // the idea here is to construct Aho-Corasick parser that will look for any data marker and run the associated regex to extract the data into state
                if (sectionDescription.Extractors.Count > 0)
                {
                    var act = new AhoCorasickDoubleArrayTrie<Action<string, LogParseState>>(sectionDescription.Extractors.Select(extractorPair =>
                        new SectionAction(
                            extractorPair.Key.ToLatin8BitEncoding(),
                            (buffer, state) =>
                            {
#if DEBUG
                                var timer = System.Diagnostics.Stopwatch.StartNew();
#endif
                                OnExtractorHit(buffer, extractorPair.Key, extractorPair.Value, state);

#if DEBUG
                                timer.Stop();
                                lock (state.ExtractorHitStats)
                                {
                                    state.ExtractorHitStats.TryGetValue(extractorPair.Key, out var stat);
                                    state.ExtractorHitStats[extractorPair.Key] = (stat.count + 1, stat.regexTime + timer.ElapsedTicks);
                                }
#endif
                            })
                    ), true);
                    parser.OnExtract = (line, buffer, state) => { act.ParseText(line, h => { h.Value(buffer, state); }); };
                }
                parsers.Add(parser);
            }
            SectionParsers = parsers.AsReadOnly();
        }

        private static void OnExtractorHit(string buffer, string trigger, Regex extractor, LogParseState state)
        {
            if (trigger == "{PPU[" || trigger == "⁂")
            {
                if (state.WipCollection["serial"] is string serial
                    && extractor.Match(buffer) is Match match
                    && match.Success
                    && match.Groups["syscall_name"].Value is string syscallName)
                {
                    lock (state)
                    {
                        if (!state.Syscalls.TryGetValue(serial, out var serialSyscallStats))
                            state.Syscalls[serial] = serialSyscallStats = new HashSet<string>();
                        serialSyscallStats.Add(syscallName);
                    }
                }
            }
            else
            {
                var matches = extractor.Matches(buffer);
                if (matches.Count == 0)
                    return;

                foreach (Match match in matches)
                foreach (Group group in match.Groups)
                {
                    if (string.IsNullOrEmpty(group.Name)
                        || group.Name == "0"
                        || string.IsNullOrWhiteSpace(group.Value))
                        continue;

                    var strValue = group.Value.ToUtf8();
                    //Config.Log.Trace($"regex {group.Name} = {group.Value}");
                    lock (state)
                    {
                        if (MultiValueItems.Contains(group.Name))
                            state.WipMultiValueCollection[group.Name].Add(strValue);
                        else
                            state.WipCollection[group.Name] = strValue;
                        if (!CountValueItems.Contains(group.Name))
                            continue;
                            
                        state.ValueHitStats.TryGetValue(group.Name, out var hits);
                        state.ValueHitStats[group.Name] = ++hits;
                    }
                }
            }
        }

        private delegate void OnNewLineDelegate(string line, string buffer, LogParseState state);

        private class LogSectionParser
        {
            public OnNewLineDelegate OnExtract = null!;
            public Action<LogParseState>? OnSectionEnd;
            public string[] EndTrigger = null!;
        }
    }
}