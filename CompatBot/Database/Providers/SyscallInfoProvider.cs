using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

using TSyscallStats = Dictionary<string, HashSet<string>>;

internal static class SyscallInfoProvider
{
    private static readonly SemaphoreSlim Limiter = new(1, 1);

    public static async ValueTask SaveAsync(TSyscallStats syscallInfo)
    {
        if (syscallInfo.Count == 0)
            return;

        if (await Limiter.WaitAsync(1000, Config.Cts.Token))
        {
            try
            {
                await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
                foreach (var productCodeMap in syscallInfo)
                {
                    var product = wdb.Thumbnail.AsNoTracking().FirstOrDefault(t => t.ProductCode == productCodeMap.Key)
                                  ?? (await wdb.Thumbnail.AddAsync(new Thumbnail {ProductCode = productCodeMap.Key}).ConfigureAwait(false)).Entity;
                    if (product.Id == 0)
                        await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);

                    foreach (var func in productCodeMap.Value)
                    {
                        var syscall = wdb.SyscallInfo.AsNoTracking().FirstOrDefault(sci => sci.Function == func.ToUtf8())
                                      ?? (await wdb.SyscallInfo.AddAsync(new SyscallInfo {Function = func.ToUtf8() }).ConfigureAwait(false)).Entity;
                        if (syscall.Id == 0)
                            await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);

                        if (!wdb.SyscallToProductMap.Any(m => m.ProductId == product.Id && m.SyscallInfoId == syscall.Id))
                            await wdb.SyscallToProductMap.AddAsync(new SyscallToProductMap {ProductId = product.Id, SyscallInfoId = syscall.Id}).ConfigureAwait(false);
                    }
                }
                await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            finally
            {
                Limiter.Release();
            }
        }
    }

    public static async ValueTask<(int funcs, int links)> FixInvalidFunctionNamesAsync()
    {
        var syscallStats = new TSyscallStats();
        int funcs, links = 0;
        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        var funcsToRemove = new List<SyscallInfo>(0);
        try
        {
            funcsToRemove = wdb.SyscallInfo.AsEnumerable().Where(sci => sci.Function.Contains('(') || sci.Function.StartsWith('“')).ToList();
            funcs = funcsToRemove.Count;
            if (funcs == 0)
                return (0, 0);

            foreach (var sci in funcsToRemove.Where(sci => sci.Function.Contains('(')))
            {
                var productIds = await wdb.SyscallToProductMap
                    .AsNoTracking()
                    .Where(m => m.SyscallInfoId == sci.Id)
                    .Select(m => m.Product.ProductCode)
                    .Distinct()
                    .ToListAsync()
                    .ConfigureAwait(false);
                links += productIds.Count;
                foreach (var productId in productIds)
                {
                    if (!syscallStats.TryGetValue(productId, out var smInfo))
                        syscallStats[productId] = smInfo = new HashSet<string>();
                    smInfo.Add(sci.Function.Split('(', 2)[0]);
                }
            }
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to build fixed syscall mappings");
            throw;
        }
            
        await SaveAsync(syscallStats).ConfigureAwait(false);
        if (!await Limiter.WaitAsync(1000, Config.Cts.Token))
            return (funcs, links);

        try
        {
            wdb.SyscallInfo.RemoveRange(funcsToRemove);
            await wdb.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to remove broken syscall mappings");
            throw;
        }
        finally
        {
            Limiter.Release();
        }
        return (funcs, links);
    }

    public static async ValueTask<(int funcs, int links)> FixDuplicatesAsync()
    {
        int funcs = 0, links = 0;
        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        var duplicateFunctionNames = await wdb.SyscallInfo.Where(sci => wdb.SyscallInfo.Count(isci => isci.Function == sci.Function) > 1).Distinct().ToListAsync().ConfigureAwait(false);
        if (duplicateFunctionNames.Count == 0)
            return (0, 0);

        if (!await Limiter.WaitAsync(1000, Config.Cts.Token))
            return (funcs, links);

        try
        {
            foreach (var dupFunc in duplicateFunctionNames)
            {
                var dups = wdb.SyscallInfo.Where(sci => sci.Function == dupFunc.Function).ToList();
                if (dups.Count < 2)
                    continue;

                var mostCommonDup = dups.Select(dup => (dup, count: wdb.SyscallToProductMap.Count(scm => scm.SyscallInfoId == dup.Id))).OrderByDescending(stat => stat.count).First().dup;
                var dupsToRemove = dups.Where(df => df.Id != mostCommonDup.Id).ToList();
                funcs += dupsToRemove.Count;
                foreach (var dupToRemove in dupsToRemove)
                {
                    var mappings = wdb.SyscallToProductMap.Where(scm => scm.SyscallInfoId == dupToRemove.Id).ToList();
                    links += mappings.Count;
                    foreach (var mapping in mappings)
                    {
                        if (!wdb.SyscallToProductMap.Any(scm => scm.ProductId == mapping.ProductId && scm.SyscallInfoId == mostCommonDup.Id))
                            await wdb.SyscallToProductMap.AddAsync(new SyscallToProductMap {ProductId = mapping.ProductId, SyscallInfoId = mostCommonDup.Id}).ConfigureAwait(false);
                    }
                }
                await wdb.SaveChangesAsync().ConfigureAwait(false);
                wdb.SyscallInfo.RemoveRange(dupsToRemove);
                await wdb.SaveChangesAsync().ConfigureAwait(false);
            }
            await wdb.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Failed to remove duplicate syscall entries");
            throw;
        }
        finally
        {
            Limiter.Release();
        }
        return (funcs, links);
    }
}