using System.Collections.Specialized;
using CompatBot.Database;

namespace CompatBot.EventHandlers.LogParsing.POCOs;

public class LogParseState
{
    public NameValueCollection? CompletedCollection;
    public NameUniqueObjectCollection<string>? CompleteMultiValueCollection;
    public NameValueCollection WipCollection = new();
    public NameUniqueObjectCollection<string> WipMultiValueCollection = new();
    public readonly Dictionary<string, int> ValueHitStats = new();
    public readonly Dictionary<string, HashSet<string>> Syscalls = new();
    public int Id = 0;
    public ErrorCode Error = ErrorCode.None;
    public readonly Dictionary<int, (Piracystring filter, string context)> FilterTriggers = new();
    public Piracystring? SelectedFilter;
    public string? SelectedFilterContext;
    public long ReadBytes;
    public long TotalBytes;
    public TimeSpan ParsingTime;
#if DEBUG
    public readonly Dictionary<string, (int count, long regexTime)> ExtractorHitStats = new();
#endif

    public enum ErrorCode
    {
        None = 0,
        PiracyDetected = 1,
        SizeLimit = 2,
        UnknownError = 3,
    }
}