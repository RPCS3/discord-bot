using System;
using CompatBot.Utils;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace Tests;

[TestFixture]
public class MemoryCacheExtensionTests
{
    [Test]
    public void GetCacheKeysTest()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
        Assert.That(cache.GetCacheKeys<int>(), Is.Empty);

        const string testVal = "vale13";
        cache.Set(13, testVal);
        Assert.Multiple(() =>
            {
                Assert.That(cache.TryGetValue(13, out string? expectedVal), Is.True);
                Assert.That(expectedVal, Is.EqualTo(testVal));
            }
        );
        Assert.That(cache.GetCacheKeys<int>(), Has.Count.EqualTo(1));
    }

    [Test]
    public void GetCacheEntriesTest()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
        Assert.That(cache.GetCacheKeys<int>(), Is.Empty);

        cache.Set(13, "val13");
        Assert.That(cache.GetCacheKeys<int>(), Has.Count.EqualTo(1));
    }
}