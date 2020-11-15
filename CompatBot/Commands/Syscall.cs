using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("syscall"), Aliases("syscalls", "cell", "sce", "scecall", "scecalls"), LimitedToSpamChannel]
    [Description("Provides information about syscalls used by games")]
    internal sealed class Syscall: BaseCommandModuleCustom
    {
        [GroupCommand]
        public async Task Search(CommandContext ctx, [RemainingText, Description("Product ID, module, or function name. **Case sensitive**")] string search)
        {
            if (string.IsNullOrEmpty(search))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "No meaningful search query provided").ConfigureAwait(false);
                return;
            }

            var productCodes = ProductCodeLookup.GetProductIds(search);
            if (productCodes.Any())
            {
                await ReturnSyscallsByGameAsync(ctx, productCodes.First()).ConfigureAwait(false);
                return;
            }

            if (ctx.User.Id == 216724245957312512UL && !search.StartsWith("sys_", StringComparison.InvariantCultureIgnoreCase))
            {
                await ctx.RespondAsync($"This is not a _syscall_, {ctx.User.Mention}").ConfigureAwait(false);
                return;
            }

            await using var db = new ThumbnailDb();
            if (db.SyscallInfo.Any(sci => sci.Function == search))
            {
                var productInfoList = db.SyscallToProductMap.AsNoTracking()
                    .Where(m => m.SyscallInfo.Function == search)
                    .AsEnumerable()
                    .Select(m => new {m.Product.ProductCode, Name = m.Product.Name?.StripMarks() ?? "???"})
                    .Distinct()
                    .ToList();
                var groupedList = productInfoList
                    .GroupBy(m => m.Name, m => m.ProductCode, StringComparer.InvariantCultureIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (groupedList.Any())
                {
                    var bigList = groupedList.Count >= Config.MaxSyscallResultLines;
                    var result = new StringBuilder();
                    var fullList = bigList ? new StringBuilder() : null;
                    result.AppendLine($"List of games using `{search}`:```");
                    var c = 0;
                    foreach (var gi in groupedList)
                    {
                        var productIds = string.Join(", ", gi.Distinct().OrderBy(pc => pc).AsEnumerable());
                        if (c < Config.MaxSyscallResultLines)
                            result.AppendLine($"{gi.Key.Trim(60)} [{productIds}]");
                        if (bigList)
                            fullList!.AppendLine($"{gi.Key} [{productIds}]");
                        c++;
                    }
                    await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
                    if (bigList)
                    {
                        await using var memoryStream = Config.MemoryStreamManager.GetStream();
                        await using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
                        await streamWriter.WriteAsync(fullList).ConfigureAwait(false);
                        await streamWriter.FlushAsync().ConfigureAwait(false);
                        memoryStream.Seek(0, SeekOrigin.Begin);
                        await ctx.RespondWithFileAsync($"{search}.txt", memoryStream, $"See attached file for full list of {groupedList.Count} entries").ConfigureAwait(false);
                    }
                }
                else
                    await ctx.RespondAsync($"No games found that use `{search}`").ConfigureAwait(false);
            }
            else
            {
                var result = new StringBuilder("Unknown entity name");
                var functions = await db.SyscallInfo.Select(sci => sci.Function).Distinct().ToListAsync().ConfigureAwait(false);
                var substrFuncs = functions.Where(f => f.Contains(search, StringComparison.InvariantCultureIgnoreCase));
                var fuzzyFuncs = functions
                    .Select(f => (name: f, score: search.GetFuzzyCoefficientCached(f)))
                    .Where(i => i.score > 0.6)
                    .OrderByDescending(i => i.score)
                    .Select(i => i.name)
                    .ToList();
                functions = substrFuncs
                    .Concat(fuzzyFuncs)
                    .Distinct()
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var functionsFound = functions.Any();
                if (functionsFound)
                {
                    result.AppendLine(", possible functions:```");
                    foreach (var f in functions)
                        result.AppendLine(f);
                    result.AppendLine("```");
                }
                await ctx.SendAutosplitMessageAsync(result).ConfigureAwait(false);
            }
        }

        [Command("rename"), RequiresBotModRole]
        [Description("Provides an option to rename function call")]
        public async Task Rename(CommandContext ctx, [Description("Old function name")] string oldFunctionName, [Description("New function name")] string newFunctionName)
        {
            await using var db = new ThumbnailDb();
            var oldMatches = await db.SyscallInfo.Where(sci => sci.Function == oldFunctionName).ToListAsync().ConfigureAwait(false);
            if (oldMatches.Count == 0)
            {
                await ctx.RespondAsync($"Function `{oldFunctionName}` could not be found").ConfigureAwait(false);
                await Search(ctx, oldFunctionName).ConfigureAwait(false);
                return;
            }
            
            if (oldMatches.Count > 1)
            {
                await ctx.RespondAsync("More than one matching function was found, I can't handle this right now 😔").ConfigureAwait(false);
                await Search(ctx, oldFunctionName).ConfigureAwait(false);
                return;
            }
            
            var conflicts = await db.SyscallInfo.Where(sce => sce.Function == newFunctionName).AnyAsync().ConfigureAwait(false);
            if (conflicts)
            {
                await ctx.RespondAsync($"There is already a function `{newFunctionName}`").ConfigureAwait(false);
                await Search(ctx, newFunctionName).ConfigureAwait(false);
                return;
            }
            
            oldMatches[0].Function = newFunctionName;
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"Function `{oldFunctionName}` was successfully renamed to `{newFunctionName}`").ConfigureAwait(false);
        }

        private static async Task ReturnSyscallsByGameAsync(CommandContext ctx, string productId)
        {
            productId = productId.ToUpperInvariant();
            await using var db = new ThumbnailDb();
            var title = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productId)?.Name;
            title = string.IsNullOrEmpty(title) ? productId : $"[{productId}] {title.Trim(40)}";
            var sysInfoList = db.SyscallToProductMap.AsNoTracking()
                .Where(m => m.Product.ProductCode == productId)
                .Select(m => m.SyscallInfo)
                .Distinct()
                .AsEnumerable()
                .OrderBy(sci => sci.Function.TrimStart('_'))
                .ToList();
            if (sysInfoList.Any())
            {
                var result = new StringBuilder();
                foreach (var sci in sysInfoList)
                    result.AppendLine(sci.Function);
                await using var memoryStream = Config.MemoryStreamManager.GetStream();
                await using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
                await streamWriter.WriteAsync(result).ConfigureAwait(false);
                await streamWriter.FlushAsync().ConfigureAwait(false);
                memoryStream.Seek(0, SeekOrigin.Begin);
                await ctx.RespondWithFileAsync($"{productId} syscalls.txt", memoryStream, $"List of syscalls used by `{title}`").ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync($"No information available for `{title}`").ConfigureAwait(false);
        }
    }
}