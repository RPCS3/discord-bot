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
    private static readonly TimeSpan CacheTime = TimeSpan.FromDays(1);
    private static readonly MemoryCache CmdStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
    private static readonly MemoryCache ExplainStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
    private static readonly MemoryCache GameStatCache = new(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromDays(1) });
    private const char PrefixSeparator = '\0';

    private static readonly SemaphoreSlim Barrier = new(1, 1);
    private static readonly SemaphoreSlim BucketLock = new(1, 1);
    private static readonly (string name, MemoryCache cache)[] AllCaches =
    {
        (nameof(CmdStatCache), CmdStatCache),
        (nameof(ExplainStatCache), ExplainStatCache),
        (nameof(GameStatCache), GameStatCache),
    };

    private static ((int y, int m, int d, int h) Key, string Value) bucketPrefix = ((0, 0, 0, 0), "");

    private static string Prefix
    {
        get
        {
            var ts = DateTime.UtcNow;
            var key = (ts.Year, ts.Month, ts.Day, ts.Hour);
            if (bucketPrefix.Key == key)
                return bucketPrefix.Value;

            if (!BucketLock.Wait(0))
                return bucketPrefix.Value;

            bucketPrefix = (key, ts.ToString("yyyyMMddHH") + PrefixSeparator);
            BucketLock.Release();
            return bucketPrefix.Value;
        }
    }
    
    public static void IncCmdStat(string qualifiedName) => IncStat(qualifiedName, CmdStatCache);
    public static void IncExplainStat(string term) => IncStat(term, ExplainStatCache);
    public static void IncGameStat(string title) => IncStat(title, GameStatCache);
    private static void IncStat(string key, MemoryCache cache)
    {
        var bucketKey = Prefix + key;
        cache.TryGetValue(bucketKey, out int stat);
        cache.Set(bucketKey, ++stat, CacheTime);
    }

    public static List<(string name, int stat)> GetCmdStats() => GetStats(CmdStatCache);
    public static List<(string name, int stat)> GetExplainStats() => GetStats(ExplainStatCache);
    public static List<(string name, int stat)> GetGameStats() => GetStats(GameStatCache);
    private static List<(string name, int stat)> GetStats(MemoryCache cache)
    {
        return cache.GetCacheKeys<string>()
            .Select(c => (name: c.Split(PrefixSeparator, 2)[^1], stat: cache.Get(c) as int?))
            .Where(s => s.stat.HasValue)
            .GroupBy(s => s.name)
            .Select(g => (name: g.Key, stat: (int)g.Sum(s => s.stat)!))
            .OrderByDescending(s => s.stat)
            .ToList();
    }
    
    public static async Task SaveAsync(bool wait = false)
    {
        if (await Barrier.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                Config.Log.Debug("Got stats saving lock");
                await using var db = new BotDb();
                foreach (var (category, cache) in AllCaches)
                {
                    var entries = cache.GetCacheEntries<string>();
                    var savedKeys = new HashSet<string>();
                    foreach (var (key, value) in entries)
                        if (savedKeys.Add(key))
                        {
                            var keyParts = key.Split(PrefixSeparator, 2);
                            var bucket = keyParts.Length == 2 ? keyParts[0] : null;
                            var statKey = keyParts[^1];
                            var statValue = (int?)value?.Value ?? 0;
                            var ts = value?.AbsoluteExpiration?.ToUniversalTime().Ticks ?? 0;

                            var currentEntry = db.Stats.FirstOrDefault(e => e.Category == category && e.Bucket == bucket && e.Key == statKey);
                            if (currentEntry is null)
                                await db.Stats.AddAsync(new()
                                {
                                    Category = category,
                                    Bucket = bucket,
                                    Key = statKey,
                                    Value = statValue,
                                    ExpirationTimestamp = ts
                                }).ConfigureAwait(false);
                            else
                            {
                                currentEntry.Value = statValue;
                                currentEntry.ExpirationTimestamp = ts;
                            }
                        }
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
                {
                    var key = entry.Key;
                    if (entry.Bucket is { Length: > 0 } bucket)
                        key = bucket + PrefixSeparator + key;
                    cache.Set(key, entry.Value, time);
                }
                else
                {
                    db.Stats.Remove(entry);
                }
            }
        }
        await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
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