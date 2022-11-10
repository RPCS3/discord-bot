using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils;

internal static class MemoryCacheExtensions
{
    public static List<T> GetCacheKeys<T>(this MemoryCache memoryCache)
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
        if (field is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return new();
        }

        var value = (IDictionary?)field.GetValue(coherentState);
        return value?.Keys.OfType<T>().ToList() ?? new List<T>();
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