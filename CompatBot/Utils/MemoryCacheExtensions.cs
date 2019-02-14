using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Utils
{
    internal static class MemoryCacheExtensions
    {
        public static List<T> GetCacheKeys<T>(this MemoryCache memoryCache)
        {
            if (memoryCache == null)
                return null;

            var field = memoryCache.GetType()
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(fi => fi.Name == "_entries");

            if (field == null)
            {
                Config.Log.Error($"Looks like {nameof(MemoryCache)} internals have changed");
                return new List<T>(0);
            }

            var value = (IDictionary)field.GetValue(memoryCache);
            return value.Keys.OfType<T>().ToList();
        }
    }
}
