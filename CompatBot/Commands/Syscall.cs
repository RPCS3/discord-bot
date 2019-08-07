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
        [Command("syscall"), Aliases("cell"), LimitedToSpamChannel]
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

            using (var db = new ThumbnailDb())
            {
                var productInfoList = await db.SyscallToProductMap.AsNoTracking()
                    .Where(m => m.SyscallInfo.Module == search || m.SyscallInfo.Function == search)
                    .Select(m => new {m.Product.ProductCode, m.Product.Name})
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
                    .ToAsyncEnumerable()
                    .OrderBy(sci => sci.Module)
                    .ThenBy(sci => sci.Function)
                    .ToList()
                    .ConfigureAwait(false);
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