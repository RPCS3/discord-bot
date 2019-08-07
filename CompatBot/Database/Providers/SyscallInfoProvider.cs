using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers
{
    internal static class SyscallInfoProvider
    {
        private static readonly SemaphoreSlim Limiter = new SemaphoreSlim(1, 1);

        public static async Task SaveAsync(Dictionary<string, Dictionary<string, HashSet<string>>> syscallInfo)
        {
            if (syscallInfo == null || syscallInfo.Count == 0)
                return;

            if (await Limiter.WaitAsync(1000, Config.Cts.Token))
            {
                try
                {
                    using (var db = new ThumbnailDb())
                    {
                        foreach (var productCodeMap in syscallInfo)
                        {
                            var product = db.Thumbnail.AsNoTracking().FirstOrDefault(t => t.ProductCode == productCodeMap.Key)
                                          ?? db.Thumbnail.Add(new Thumbnail {ProductCode = productCodeMap.Key}).Entity;
                            foreach (var moduleMap in productCodeMap.Value)
                            foreach (var func in moduleMap.Value)
                            {
                                var syscall = db.SyscallInfo.AsNoTracking().FirstOrDefault(sci => sci.Module == moduleMap.Key && sci.Function == func)
                                              ?? db.SyscallInfo.Add(new SyscallInfo {Module = moduleMap.Key, Function = func}).Entity;
                                if (!db.SyscallToProductMap.Any(m => m.ProductId == product.Id && m.SyscallInfoId == syscall.Id))
                                    db.SyscallToProductMap.Add(new SyscallToProductMap {ProductId = product.Id, SyscallInfoId = syscall.Id});
                            }
                        }
                        await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    Limiter.Release();
                }
            }
        }
    }
}
