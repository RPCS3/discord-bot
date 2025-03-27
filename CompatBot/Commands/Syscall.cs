using System.IO;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.EventHandlers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("syscall"), TextAlias("syscalls", "cell", "sce", "scecall", "scecalls"), LimitedToSpamChannel]
internal static class Syscall
{
    [Command("search")]
    [Description("Get information about system and firmware calls used by games")]
    public static async ValueTask Search(
        SlashCommandContext ctx,
        [Description("Product ID, module, or function name. **Case sensitive**")]
        string search
    )
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(true);
        var productCodes = ProductCodeLookup.GetProductIds(search);
        if (productCodes.Count > 0)
        {
            await ReturnSyscallsByGameAsync(ctx, productCodes[0], ephemeral).ConfigureAwait(false);
            return;
        }

        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        if (db.SyscallInfo.Any(sci => sci.Function == search))
        {
            var productInfoList = db.SyscallToProductMap
                .AsNoTracking()
                .Where(m => m.SyscallInfo.Function == search)
                .Include(m => m.Product)
                .AsEnumerable()
                .Select(m => new {m.Product.ProductCode, Name = m.Product.Name?.StripMarks() ?? "???"})
                .Distinct()
                .ToList();
            var groupedList = productInfoList
                .GroupBy(m => m.Name, m => m.ProductCode, StringComparer.InvariantCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupedList.Count > 0)
            {
                var bigList = groupedList.Count >= Config.MaxSyscallResultLines;
                var result = new StringBuilder();
                var fullList = bigList ? new StringBuilder() : result;
                result.AppendLine($"List of games using `{search}`:```");
                var c = 0;
                foreach (var gi in groupedList)
                {
                    var productIds = string.Join(", ", gi.Distinct().OrderBy(pc => pc).AsEnumerable());
                    if (c < Config.MaxSyscallResultLines)
                        result.AppendLine($"{gi.Key.Trim(60)} [{productIds}]");
                    if (bigList)
                        fullList.AppendLine($"{gi.Key} [{productIds}]");
                    c++;
                }
                if (bigList || result.Length > EmbedPager.MaxMessageLength)
                {
                    await using var memoryStream = Config.MemoryStreamManager.GetStream();
                    await using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
                    await streamWriter.WriteAsync(fullList).ConfigureAwait(false);
                    await streamWriter.FlushAsync().ConfigureAwait(false);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var response = new DiscordInteractionResponseBuilder()
                        .WithContent($"See attached file for full list of {groupedList.Count} entries")
                        .AddFile($"{search}.txt", memoryStream)
                        .AsEphemeral(ephemeral);
                    await ctx.RespondAsync(response).ConfigureAwait(false);
                }
                else
                    await ctx.RespondAsync(result.Append("```").ToString(), ephemeral: ephemeral).ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync($"No games found that use `{search}`", ephemeral: ephemeral).ConfigureAwait(false);
        }
        else
        {
            var result = new StringBuilder("Unknown entity name");
            var functions = await db.SyscallInfo
                .AsNoTracking()
                .Select(sci => sci.Function)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);
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
            if (functions.Count > 0)
            {
                result.AppendLine(", possible functions:```");
                foreach (var f in functions)
                    result.AppendLine(f);
                result.AppendLine("```");
            }
            var pages = AutosplitResponseHelper.AutosplitMessage(result.ToString());
            await ctx.RespondAsync(pages[0], ephemeral: ephemeral).ConfigureAwait(false);
        }
    }

    [Command("rename"), RequiresBotModRole]
    [Description("Provides an option to rename function call")]
    public static async ValueTask Rename(
        SlashCommandContext ctx,
        [Description("Old function name")]
        string oldFunctionName,
        [Description("New function name")]
        string newFunctionName
    )
    {
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        var oldMatches = await db.SyscallInfo.Where(sci => sci.Function == oldFunctionName).ToListAsync().ConfigureAwait(false);
        if (oldMatches.Count is 0)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Function `{oldFunctionName}` could not be found", ephemeral: true).ConfigureAwait(false);
            return;
        }
            
        if (oldMatches.Count > 1)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} More than one matching function was found, I can't handle this right now 😔", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (await db.SyscallInfo.Where(sce => sce.Function == newFunctionName).AnyAsync().ConfigureAwait(false))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} There is already a function `{newFunctionName}`", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var ephemeral = !ctx.Channel.IsSpamChannel();
        oldMatches[0].Function = newFunctionName;
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Function `{oldFunctionName}` was successfully renamed to `{newFunctionName}`", ephemeral: ephemeral).ConfigureAwait(false);
    }

    private static async ValueTask ReturnSyscallsByGameAsync(SlashCommandContext ctx, string productId, bool ephemeral)
    {
        productId = productId.ToUpperInvariant();
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        var title = db.Thumbnail.FirstOrDefault(t => t.ProductCode == productId)?.Name;
        title = string.IsNullOrEmpty(title) ? productId : $"[{productId}] {title.Trim(40)}";
        var sysInfoList = db.SyscallToProductMap.AsNoTracking()
            .Where(m => m.Product.ProductCode == productId)
            .Select(m => m.SyscallInfo)
            .Distinct()
            .AsEnumerable()
            .OrderBy(sci => sci.Function.TrimStart('_'))
            .ToList();
        if (sysInfoList.Count > 0)
        {
            var result = new StringBuilder();
            foreach (var sci in sysInfoList)
                result.AppendLine(sci.Function);
            await using var memoryStream = Config.MemoryStreamManager.GetStream();
            await using var streamWriter = new StreamWriter(memoryStream, Encoding.UTF8);
            await streamWriter.WriteAsync(result).ConfigureAwait(false);
            await streamWriter.FlushAsync().ConfigureAwait(false);
            memoryStream.Seek(0, SeekOrigin.Begin);
            var response = new DiscordInteractionResponseBuilder()
                .WithContent($"List of syscalls used by `{title}`")
                .AddFile($"{productId} syscalls.txt", memoryStream)
                .AsEphemeral(ephemeral);
            await ctx.RespondAsync(response).ConfigureAwait(false);
        }
        else
            await ctx.RespondAsync($"No information available for `{title}`", ephemeral: ephemeral).ConfigureAwait(false);
    }
}