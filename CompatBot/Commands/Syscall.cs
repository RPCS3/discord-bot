using System;
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
    internal sealed class Syscall: BaseCommandModuleCustom
    {
        [Command("syscall"), Aliases("syscalls", "cell", "sce", "scecall", "scecalls"), LimitedToSpamChannel]
        [Description("Provides information about syscalls used by games")]
        public async Task Search(CommandContext ctx,[RemainingText, Description("Product ID, module, or function name. **Case sensitive**")] string search)
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

            using (var db = new ThumbnailDb())
            {
                if (db.SyscallInfo.Any(sci => sci.Module == search || sci.Function == search))
                {
                    var productInfoList = await db.SyscallToProductMap.AsNoTracking()
                        .Where(m => m.SyscallInfo.Module == search || m.SyscallInfo.Function == search)
                        .Select(m => new {m.Product.ProductCode, m.Product.Name})
                        .Distinct()
                        .ToAsyncEnumerable()
                        .OrderBy(i => i.Name, StringComparer.InvariantCultureIgnoreCase)
                        .ThenBy(i => i.ProductCode)
                        .ToList()
                        .ConfigureAwait(false);
                    if (productInfoList.Any())
                    {
                        var result = new StringBuilder($"List of games using `{search}`:```").AppendLine();
                        foreach (var gi in productInfoList)
                            result.AppendLine($"[{gi.ProductCode}] {gi.Name.Trim(40)}");
                        await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
                    }
                    else
                        await ctx.RespondAsync($"No games found that use `{search}`").ConfigureAwait(false);
                }
                else
                {
                    var result = new StringBuilder("Unknown entity name");
                    var modules = await db.SyscallInfo.Select(sci => sci.Module).Distinct().ToListAsync().ConfigureAwait(false);
                    var substrModules = modules.Where(m => m.Contains(search, StringComparison.CurrentCultureIgnoreCase));
                    var fuzzyModules = modules
                        .Select(m => (name: m, score: search.GetFuzzyCoefficientCached(m)))
                        .Where(i => i.score > 0.6)
                        .OrderByDescending(i => i.score)
                        .Select(i => i.name)
                        .ToList();
                    modules = substrModules.Concat(fuzzyModules).Distinct().ToList();
                    var modulesFound = modules.Any();
                    if (modulesFound)
                    {
                        result.AppendLine(", possible modules:```");
                        foreach (var m in modules)
                            result.AppendLine(m);
                        result.AppendLine("```");
                    }

                    var functions = await db.SyscallInfo.Select(sci => sci.Function).Distinct().ToListAsync().ConfigureAwait(false);
                    var substrFuncs = functions.Where(f => f.Contains(search, StringComparison.InvariantCultureIgnoreCase));
                    var fuzzyFuncs = functions
                        .Select(f => (name: f, score: search.GetFuzzyCoefficientCached(f)))
                        .Where(i => i.score > 0.6)
                        .OrderByDescending(i => i.score)
                        .Select(i => i.name)
                        .ToList();
                    functions = substrFuncs.Concat(fuzzyFuncs).Distinct().ToList();
                    var functionsFound = functions.Any();
                    if (functionsFound)
                    {
                        if (modulesFound)
                            result.AppendLine("Possible functions:```");
                        else
                            result.AppendLine(", possible functions:```");
                        foreach (var f in functions)
                            result.AppendLine(f);
                        result.AppendLine("```");
                    }
                    await ctx.SendAutosplitMessageAsync(result).ConfigureAwait(false);
                }
            }
        }

        private async Task ReturnSyscallsByGameAsync(CommandContext ctx, string productId)
        {
            productId = productId.ToUpperInvariant();
            string title = null;
            using (var db = new ThumbnailDb())
            {
                title = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productId)?.Name;
                title = string.IsNullOrEmpty(title) ? productId : $"[{productId}] {title.Trim(40)}";
                var sysInfoList = await db.SyscallToProductMap.AsNoTracking()
                    .Where(m => m.Product.ProductCode == productId)
                    .Select(m => m.SyscallInfo)
                    .Distinct()
                    .ToAsyncEnumerable()
                    .OrderBy(sci => sci.Module)
                    .ThenBy(sci => sci.Function)
                    .ToList()
                    .ConfigureAwait(false);
                if (ctx.User.Id == 216724245957312512UL)
                    sysInfoList = sysInfoList.Where(i => i.Function.StartsWith("sys_", StringComparison.InvariantCultureIgnoreCase)).ToList();
                if (sysInfoList.Any())
                {
                    var result = new StringBuilder($"List of syscalls used by `{title}`:```").AppendLine();
                    foreach (var sci in sysInfoList)
                        result.AppendLine($"{sci.Module}: {sci.Function}");
                    await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
                }
                else
                    await ctx.RespondAsync($"No information available for `{title}`").ConfigureAwait(false);
            }
        }
    }
}