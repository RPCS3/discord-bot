using System;
using System.Collections.Generic;
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
        Assert.That(cache.GetCacheKeys<int>().Count, Is.EqualTo(0));

        cache.Set(13, "val13");
        Assert.That(cache.GetCacheKeys<int>().Count, Is.EqualTo(1));
    }

    [Test]
    public void GetCacheEntriesTest()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { ExpirationScanFrequency = TimeSpan.FromHours(1) });
        Assert.That(cache.GetCacheKeys<int>().Count, Is.EqualTo(0));

        cache.Set(13, "val13");
        Assert.That(cache.GetCacheKeys<int>().Count, Is.EqualTo(1));
    }
}