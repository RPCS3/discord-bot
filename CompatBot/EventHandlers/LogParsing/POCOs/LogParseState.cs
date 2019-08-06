using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using CompatBot.Database;

namespace CompatBot.EventHandlers.LogParsing.POCOs
{
    public class LogParseState
    {
        public NameValueCollection CompleteCollection = null;
        public NameValueCollection WipCollection = new NameValueCollection();
        public readonly Dictionary<string, int> ValueHitStats = new Dictionary<string, int>();
        public int Id = 0;
        public ErrorCode Error = ErrorCode.None;
        public readonly Dictionary<int, (Piracystring filter, string context)> FilterTriggers = new Dictionary<int, (Piracystring filter, string context)>();
        public Piracystring SelectedFilter;
        public string SelectedFilterContext;
        public long ReadBytes;
        public long TotalBytes;
        public int LinesAfterConfig;
        public TimeSpan ParsingTime;
#if DEBUG
        public readonly Dictionary<string, int> ExtractorHitStats = new Dictionary<string, int>();
#endif

        public enum ErrorCode
        {
            None = 0,
            PiracyDetected = 1,
            SizeLimit = 2,
        }
    }
}