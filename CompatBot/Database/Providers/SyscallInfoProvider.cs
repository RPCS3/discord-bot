using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

using TSyscallStats = Dictionary<string, HashSet<string>>;

internal static class SyscallInfoProvider
{
    private static readonly SemaphoreSlim Limiter = new(1, 1);

    public static async Task SaveAsync(TSyscallStats syscallInfo)
    {
        if (syscallInfo.Count == 0)
            return;

        if (await Limiter.WaitAsync(1000, Config.Cts.Token))
        {
            try
            {
                await using var db = ThumbnailDb.OpenWrite();
                foreach (var productCodeMap in syscallInfo)
                {
                    var product = db.Thumbnail.AsNoTracking().FirstOrDefault(t => t.ProductCode == productCodeMap.Key)
                                  ?? (await db.Thumbnail.AddAsync(new Thumbnail {ProductCode = productCodeMap.Key}).ConfigureAwait(false)).Entity;
                    if (product.Id == 0)
                        await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);

                    foreach (var func in productCodeMap.Value)
                    {
                        var syscall = db.SyscallInfo.AsNoTracking().FirstOrDefault(sci => sci.Function == func.ToUtf8())
                                      ?? (await db.SyscallInfo.AddAsync(new SyscallInfo {Function = func.ToUtf8() }).ConfigureAwait(false)).Entity;
                        if (syscall.Id == 0)
                            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);

                        if (!db.SyscallToProductMap.Any(m => m.ProductId == product.Id && m.SyscallInfoId == syscall.Id))
                            await db.SyscallToProductMap.AddAsync(new SyscallToProductMap {ProductId = product.Id, SyscallInfoId = syscall.Id}).ConfigureAwait(false);
                    }
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            finally
            {
                Limiter.Release();
            }
        }
    }

    public static async Task<(int funcs, int links)> FixInvalidFunctionNamesAsync()
    {
        var syscallStats = new TSyscallStats();
        int funcs, links = 0;
        await using var db = ThumbnailDb.OpenWrite();
        var funcsToRemove = new List<SyscallInfo>(0);
        try
        {
            funcsToRemove = db.SyscallInfo.AsEnumerable().Where(sci => sci.Function.Contains('(') || sci.Function.StartsWith('“')).ToList();
            funcs = funcsToRemove.Count;
            if (funcs == 0)
                return (0, 0);

            foreach (var sci in funcsToRemove.Where(sci => sci.Function.Contains('(')))
            {
                var productIds = await db.SyscallToProductMap
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
            db.SyscallInfo.RemoveRange(funcsToRemove);
            await db.SaveChangesAsync().ConfigureAwait(false);
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

    public static async Task<(int funcs, int links)> FixDuplicatesAsync()
    {
        int funcs = 0, links = 0;
        await using var db = ThumbnailDb.OpenWrite();
        var duplicateFunctionNames = await db.SyscallInfo.Where(sci => db.SyscallInfo.Count(isci => isci.Function == sci.Function) > 1).Distinct().ToListAsync().ConfigureAwait(false);
        if (duplicateFunctionNames.Count == 0)
            return (0, 0);

        if (!await Limiter.WaitAsync(1000, Config.Cts.Token))
            return (funcs, links);

        try
        {
            foreach (var dupFunc in duplicateFunctionNames)
            {
                var dups = db.SyscallInfo.Where(sci => sci.Function == dupFunc.Function).ToList();
                if (dups.Count < 2)
                    continue;

                var mostCommonDup = dups.Select(dup => (dup, count: db.SyscallToProductMap.Count(scm => scm.SyscallInfoId == dup.Id))).OrderByDescending(stat => stat.count).First().dup;
                var dupsToRemove = dups.Where(df => df.Id != mostCommonDup.Id).ToList();
                funcs += dupsToRemove.Count;
                foreach (var dupToRemove in dupsToRemove)
                {
                    var mappings = db.SyscallToProductMap.Where(scm => scm.SyscallInfoId == dupToRemove.Id).ToList();
                    links += mappings.Count;
                    foreach (var mapping in mappings)
                    {
                        if (!db.SyscallToProductMap.Any(scm => scm.ProductId == mapping.ProductId && scm.SyscallInfoId == mostCommonDup.Id))
                            await db.SyscallToProductMap.AddAsync(new SyscallToProductMap {ProductId = mapping.ProductId, SyscallInfoId = mostCommonDup.Id}).ConfigureAwait(false);
                    }
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
                db.SyscallInfo.RemoveRange(dupsToRemove);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
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