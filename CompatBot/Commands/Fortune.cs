using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CompatApiClient.Compression;
using CompatBot.Database;
using ConcurrentCollections;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("fortune")]
internal static class Fortune
{
    private static readonly SemaphoreSlim ImportCheck = new(1, 1);

    [Command("open")]
    [Description("Get a personal fortune cookie message once a day")]
    public static async ValueTask ShowFortune(SlashCommandContext ctx)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel() && !ctx.Channel.IsOfftopicChannel();
        if (await GetFortuneAsync(ctx.User).ConfigureAwait(false) is {Length: >0} fortune)
            await ctx.RespondAsync(fortune, ephemeral: ephemeral).ConfigureAwait(false);
        else
            await ctx.RespondAsync($"{Config.Reactions.Failure} There are no fortunes to tell", ephemeral: true).ConfigureAwait(false);
    }

    [Command("import"), RequiresBotModRole]
    [Description("Import new fortunes from a standard UNIX fortune file")]
    public static async ValueTask Import(
        SlashCommandContext ctx,
        [Description("Link to a plain text file"), MinMaxLength(12)] string? url = null,
        [Description("Text file in UNIX fortunes format")] DiscordAttachment? attachment = null)
    {
        if (!await ImportCheck.WaitAsync(0).ConfigureAwait(false))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} There is another import in progress already").ConfigureAwait(false);
            return;
        }
     
        using var timeouCts = new CancellationTokenSource(TimeSpan.FromSeconds(15*60-5));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(timeouCts.Token, Config.Cts.Token);
        try
        {
            url ??= attachment?.Url;
            if (string.IsNullOrEmpty(url))
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} At least one source must be provided").ConfigureAwait(false);
                return;
            }

            await ctx.RespondAsync("Importing…", ephemeral: true).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();
            await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
            using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var buf = new StringBuilder();
            string? line;
            int count = 0, skipped = 0;
            var allFortunes = new ConcurrentHashSet<string>(
                await wdb.Fortune.AsNoTracking().Select(f => f.Content).ToListAsync(cancellationToken: cts.Token).ConfigureAwait(false),
                StringComparer.OrdinalIgnoreCase
            );

            while (
                !cts.IsCancellationRequested
                && ((line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false)) != null
                    || buf.Length > 0)
            )
            {
                if (line is "%" or null)
                {
                    var newFortune = buf.ToString().Replace("\r\n", "\n").Trim();
                    if (newFortune.Length > 200)
                    {
                        buf.Clear();
                        skipped++;
                        continue;
                    }

                    if (allFortunes.Contains(newFortune))
                    {
                        buf.Clear();
                        skipped++;
                        continue;
                    }

                    var duplicate = allFortunes
                        .AsParallel()
                        .WithCancellation(cts.Token)
                        .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount - 2))
                        .Any(f => f.GetFuzzyCoefficientCached(newFortune) >= 0.95);
                    if (duplicate)
                    {
                        buf.Clear();
                        skipped++;
                        continue;
                    }

                    await wdb.Fortune.AddAsync(new() {Content = newFortune}, cts.Token).ConfigureAwait(false);
                    allFortunes.Add(newFortune);
                    buf.Clear();
                    count++;
                }
                else
                    buf.AppendLine(line);
                if (line is null)
                    break;

                if (stopwatch.ElapsedMilliseconds > 10_000)
                {
                    var progressMsg = $"Imported {count} fortune{(count == 1 ? "" : "s")}";
                    if (skipped > 0)
                        progressMsg += $", skipped {skipped}";
                    if (response.Content.Headers.ContentLength is long len and > 0)
                        progressMsg += $" ({stream.Position * 100.0 / len:0.##}%)";
                    await ctx.EditResponseAsync(progressMsg).ConfigureAwait(false);
                    stopwatch.Restart();
                }
            }
            await wdb.SaveChangesAsync(cts.Token).ConfigureAwait(false);
            var result = $"{Config.Reactions.Success} Imported {count} fortune{(count == 1 ? "" : "s")}";
            if (skipped > 0)
                result += $", skipped {skipped}";
            await ctx.EditResponseAsync(result).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await ctx.EditResponseAsync($"{Config.Reactions.Failure} Failed to import data: " + e.Message).ConfigureAwait(false);
            return;
        }
        finally
        {
            ImportCheck.Release();
        }
        if (cts.IsCancellationRequested)
            await ctx.EditResponseAsync($"{Config.Reactions.Failure} Reached time limit for discord interaction").ConfigureAwait(false);
    }

    [Command("export"), RequiresBotModRole]
    [Description("Export fortune database into UNIX fortune format file")]
    public static async ValueTask Export(SlashCommandContext ctx)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        try
        {
            var count = 0;
            await using var outputStream = Config.MemoryStreamManager.GetStream();
            await using var writer = new StreamWriter(outputStream);
            await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
            foreach (var fortune in db.Fortune.AsNoTracking())
            {
                if (Config.Cts.Token.IsCancellationRequested)
                    break;
                    
                await writer.WriteAsync(fortune.Content).ConfigureAwait(false);
                await writer.WriteAsync("\n%\n").ConfigureAwait(false);
                count++;
            }
            await writer.FlushAsync().ConfigureAwait(false);
            outputStream.Seek(0, SeekOrigin.Begin);
            var builder = new DiscordInteractionResponseBuilder()
                .AsEphemeral(ephemeral)
                .WithContent($"Exported {count} fortune{(count == 1 ? "": "s")}")
                .AddFile("fortunes.txt", outputStream);
            await ctx.RespondAsync(builder).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to export data: " + e.Message, ephemeral: ephemeral).ConfigureAwait(false);
        }
    }

    [Command("clear"), RequiresBotModRole]
    [Description("Clear fortune database")]
    public static async ValueTask Clear(SlashCommandContext ctx, [Description("Must be `with my blessing, I swear I exported the backup`")] string confirmation)
    {
        if (confirmation is not "with my blessing, I swear I exported the backup")
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Incorrect confirmation", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await ctx.DeferResponseAsync(true).ConfigureAwait(false);
        await using var wdb = await ThumbnailDb.OpenWriteAsync().ConfigureAwait(false);
        wdb.Fortune.RemoveRange(wdb.Fortune);
        var count = await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Removed {count} fortune{(count == 1 ? "" : "s")}", ephemeral: true).ConfigureAwait(false);
    }

    public static async ValueTask<string?> GetFortuneAsync(DiscordUser user)
    {
        var prefix = DateTime.UtcNow.ToString("yyyyMMdd")+ user.Id.ToString("x16");
        var rng = new Random(prefix.GetStableHash());
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        Database.Fortune? fortune;
        do
        {
            var totalFortunes = await db.Fortune.CountAsync().ConfigureAwait(false);
            if (totalFortunes == 0)
                return null;
                
            var selectedId = rng.Next(totalFortunes);
            fortune = await db.Fortune.AsNoTracking().Skip(selectedId).FirstOrDefaultAsync().ConfigureAwait(false);
        } while (fortune is null);

        var tmp = new StringBuilder();
        var quote = true;
        foreach (var l in fortune.Content.FixTypography().Split('\n'))
        {
            quote &= !l.StartsWith("    ");
            if (quote)
                tmp.Append("> ");
            tmp.Append(l).Append('\n');
        }
        return $"""
                {user.Mention}, your fortune for today:
                {tmp.ToString().TrimEnd().FixSpaces()}
                """;
    }
}
