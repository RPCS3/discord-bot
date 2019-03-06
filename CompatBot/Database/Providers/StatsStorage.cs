using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Database.Providers
{
    internal static class StatsStorage
    {
        internal static readonly TimeSpan CacheTime = TimeSpan.FromDays(1);
        internal static readonly MemoryCache CmdStatCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
        internal static readonly MemoryCache ExplainStatCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
        internal static readonly MemoryCache GameStatCache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });

        private static readonly SemaphoreSlim barrier = new SemaphoreSlim(1, 1);
        private static readonly MemoryCache[] AllCaches = { CmdStatCache, ExplainStatCache, GameStatCache };

        public static async Task SaveAsync(bool wait = false)
        {
            if (await barrier.WaitAsync(0).ConfigureAwait(false))
            {
                try
                {
                    Config.Log.Debug("Got stats saving lock");
                    using (var db = new BotDb())
                    {
                        db.Stats.RemoveRange(db.Stats);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                        foreach (var cache in AllCaches)
                        {
                            var category = cache.GetType().Name;
                            var entries = cache.GetCacheEntries<string>();
                            var savedKeys = new HashSet<string>();
                            foreach (var entry in entries)
                                if (savedKeys.Add(entry.Key))
                                    await db.Stats.AddAsync(new Stats
                                    {
                                        Category = category,
                                        Key = entry.Key,
                                        Value = ((int?) entry.Value.Value) ?? 0,
                                        ExpirationTimestamp = entry.Value.AbsoluteExpiration?.ToUniversalTime().Ticks ?? 0
                                    }).ConfigureAwait(false);
                                else
                                    Config.Log.Warn($"Somehow there's another '{entry.Key}' in the {category} cache");
                        }
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
                catch(Exception e)
                {
                    Config.Log.Error(e, "Failed to save user stats");
                }
                finally
                {
                    barrier.Release();
                    Config.Log.Debug("Released stats saving lock");
                }
            }
            else if (wait)
            {
                await barrier.WaitAsync().ConfigureAwait(false);
                barrier.Release();
            }
        }

        public static async Task RestoreAsync()
        {
            var now = DateTime.UtcNow;
            using (var db = new BotDb())
                foreach (var cache in AllCaches)
                {
                    var category = cache.GetType().Name;
                    var entries = await db.Stats.Where(e => e.Category == category).ToListAsync().ConfigureAwait(false);
                    foreach (var entry in entries)
                    {
                        var time = entry.ExpirationTimestamp.AsUtc();
                        if (time > now)
                            cache.Set(entry.Key, entry.Value, time);
                    }
                }
        }

        public static async Task BackgroundSaveAsync()
        {
            while (!Config.Cts.IsCancellationRequested)
            {
                await Task.Delay(60 * 60 * 1000, Config.Cts.Token).ConfigureAwait(false);
                if (!Config.Cts.IsCancellationRequested)
                    await SaveAsync().ConfigureAwait(false);
            }
        }
    }
}
