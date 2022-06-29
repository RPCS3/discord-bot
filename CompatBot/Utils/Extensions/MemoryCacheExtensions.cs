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
        var field = memoryCache.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_entries");

        if (field is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return new List<T>();
        }

        var value = (IDictionary?)field.GetValue(memoryCache);
        return value?.Keys.OfType<T>().ToList() ?? new List<T>();
    }

    public static Dictionary<TKey, ICacheEntry?> GetCacheEntries<TKey>(this MemoryCache memoryCache)
        where TKey: notnull
    {
        var field = memoryCache.GetType()
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(fi => fi.Name == "_entries");

        var cacheEntries = (IDictionary?)field?.GetValue(memoryCache);
        if (cacheEntries is null)
        {
            Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
            return new Dictionary<TKey, ICacheEntry?>(0);
        }

        var result = new Dictionary<TKey, ICacheEntry?>(cacheEntries.Count);
        foreach (DictionaryEntry e in cacheEntries)
            result.Add((TKey)e.Key, (ICacheEntry?)e.Value);
        return result;
    }
}