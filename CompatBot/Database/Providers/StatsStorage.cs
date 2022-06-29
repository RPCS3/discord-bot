using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Database.Providers;

internal static class StatsStorage
{
    internal static readonly TimeSpan CacheTime = TimeSpan.FromDays(1);
    internal static readonly MemoryCache CmdStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
    internal static readonly MemoryCache ExplainStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
    internal static readonly MemoryCache GameStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });

    private static readonly SemaphoreSlim Barrier = new(1, 1);
    private static readonly (string name, MemoryCache cache)[] AllCaches =
    {
        (nameof(CmdStatCache), CmdStatCache),
        (nameof(ExplainStatCache), ExplainStatCache),
        (nameof(GameStatCache), GameStatCache),
    };

    public static async Task SaveAsync(bool wait = false)
    {
        if (await Barrier.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                Config.Log.Debug("Got stats saving lock");
                await using var db = new BotDb();
                db.Stats.RemoveRange(db.Stats);
                await db.SaveChangesAsync().ConfigureAwait(false);
                foreach (var (category, cache) in AllCaches)
                {
                    var entries = cache.GetCacheEntries<string>();
                    var savedKeys = new HashSet<string>();
                    foreach (var (key, value) in entries)
                        if (savedKeys.Add(key))
                            await db.Stats.AddAsync(new Stats
                            {
                                Category = category,
                                Key = key,
                                Value = (int?)value?.Value ?? 0,
                                ExpirationTimestamp = value?.AbsoluteExpiration?.ToUniversalTime().Ticks ?? 0
                            }).ConfigureAwait(false);
                        else
                            Config.Log.Warn($"Somehow there's another '{key}' in the {category} cache");
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch(Exception e)
            {
                Config.Log.Error(e, "Failed to save user stats");
            }
            finally
            {
                Barrier.Release();
                Config.Log.Debug("Released stats saving lock");
            }
        }
        else if (wait)
        {
            await Barrier.WaitAsync().ConfigureAwait(false);
            Barrier.Release();
        }
    }

    public static async Task RestoreAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = new BotDb();
        foreach (var (category, cache) in AllCaches)
        {
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