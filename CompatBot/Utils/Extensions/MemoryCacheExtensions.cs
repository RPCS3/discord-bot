using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils;

internal static class MemoryCacheExtensions
{
    private static readonly object throaway = new();
    
    public static List<T> GetCacheKeys<T>(this MemoryCache memoryCache)
        => memoryCache.Keys.OfType<T>().ToList();

    public static Dictionary<TKey, ICacheEntry?> GetCacheEntries<TKey>(this MemoryCache memoryCache)
        where TKey: notnull
    {
        //memoryCache.TryGetValue();
        var stateField = memoryCache.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_coherentState");
        var coherentState = stateField?.GetValue(memoryCache);
        if (coherentState is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed in {nameof(coherentState)}");
            return new();
        }

        var entriesName = "_nonStringEntries";
        if (typeof(TKey) == typeof(string))
            entriesName = "_stringEntries";
        var entriesField = coherentState.GetType() // MemoryCache.CoherentState
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == entriesName);
        var cacheEntries = (IDictionary?)entriesField?.GetValue(coherentState);
        if (cacheEntries is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed in {nameof(coherentState)} cache entries");
            return new(0);
        }

        var result = new Dictionary<TKey, ICacheEntry?>(cacheEntries.Count);
        foreach (DictionaryEntry e in cacheEntries)
            result.Add((TKey)e.Key, (ICacheEntry?)e.Value);
        return result;
    }
}