using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils;

internal static class MemoryCacheExtensions
{
    private static readonly object throaway = new object();
    
    public static List<T> GetCacheKeys<T>(this MemoryCache memoryCache)
    {
        // idk why it requires access before it populates the internal state
        memoryCache.TryGetValue("str", out _);
        memoryCache.TryGetValue(throaway, out _);
        
        // get the internal state object
        var stateField = memoryCache.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_coherentState");
        var coherentState = stateField?.GetValue(memoryCache);
        if (coherentState is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return [];
        }

        // get the actual underlying key-value object
        var stringField = coherentState.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_stringEntries");
        var nonStringField = coherentState.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_nonStringEntries");
        if (stringField is null || nonStringField is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return [];
        }

        // read the keys
        var value = typeof(T) == typeof(string)
            ? (IDictionary?)stringField.GetValue(coherentState)
            : (IDictionary?)nonStringField.GetValue(coherentState);
        return value?.Keys.OfType<T>().ToList() ?? [];
    }

    public static Dictionary<TKey, ICacheEntry?> GetCacheEntries<TKey>(this MemoryCache memoryCache)
        where TKey: notnull
    {
        var stateField = memoryCache.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_coherentState");
        var coherentState = stateField?.GetValue(memoryCache);
        if (coherentState is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return new();
        }

        var field = coherentState.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_entries");
        var cacheEntries = (IDictionary?)field?.GetValue(coherentState);
        if (cacheEntries is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return new(0);
        }

        var result = new Dictionary<TKey, ICacheEntry?>(cacheEntries.Count);
        foreach (DictionaryEntry e in cacheEntries)
            result.Add((TKey)e.Key, (ICacheEntry?)e.Value);
        return result;
    }
}