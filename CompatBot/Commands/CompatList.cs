using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompatApiClient;
using CompatApiClient.Compression;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils.ResultFormatters;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class CompatList
{
    private static readonly Client Client = new();
    private static readonly GithubClient.Client GithubClient = new(Config.GithubToken);
    private static readonly SemaphoreSlim UpdateCheck = new(1, 1);
    private static string? lastUpdateInfo, lastFullBuildNumber;
    private const string Rpcs3UpdateStateKey = "Rpcs3UpdateState";
    private const string Rpcs3UpdateBuildKey = "Rpcs3UpdateBuild";
    private static UpdateInfo? cachedUpdateInfo;
    [GeneratedRegex(@"v(?<version>\d+\.\d+\.\d+)-(?<build>\d+)-(?<commit>[0-9a-f]+)\b", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex UpdateVersionRegex();
    [GeneratedRegex(@"\b(demo|trial)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    internal static partial Regex TrialNamePattern();

    static CompatList()
    {
        using var db = BotDb.OpenRead();
        lastUpdateInfo = db.BotState.FirstOrDefault(k => k.Key == Rpcs3UpdateStateKey)?.Value;
        lastFullBuildNumber = db.BotState.FirstOrDefault(k => k.Key == Rpcs3UpdateBuildKey)?.Value;
        //lastUpdateInfo = "8022";
        if (lastUpdateInfo is {Length: >0} strPr && int.TryParse(strPr, out var pr))
        {
            try
            {
                var prInfo = GithubClient.GetPrInfoAsync(pr, Config.Cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                cachedUpdateInfo = Client.GetUpdateAsync(Config.Cts.Token, prInfo?.MergeCommitSha).ConfigureAwait(false).GetAwaiter().GetResult();
                if (cachedUpdateInfo?.CurrentBuild == null)
                    return;
                
                cachedUpdateInfo.LatestBuild = cachedUpdateInfo.CurrentBuild;
                cachedUpdateInfo.CurrentBuild = null;
            }
            catch { }
        }
    }

    [Command("compatibility")]
    [Description("Search the game compatibility list")]
    public static async ValueTask Compat(
        SlashCommandContext ctx,
        [Description("Game title or product code to look up")]
        [SlashAutoCompleteProvider<ProductCodeAutoCompleteProvider>]
        string title
    )
    {
        if (await ContentFilter.FindTriggerAsync(FilterContext.Chat, title).ConfigureAwait(false) is not null)
        {
            await ctx.RespondAsync("Invalid game title or product code.", true).ConfigureAwait(false);
            return;
        }
        var ephemeral = !ctx.Channel.IsSpamChannel();
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);

        var productCodes = ProductCodeLookup.GetProductIds(title);
        if (productCodes.Count > 0)
        {
            var formattedResults = await ProductCodeLookup.LookupProductCodeAndFormatAsync(ctx.Client, productCodes).ConfigureAwait(false);
            await ctx.RespondAsync(embed: formattedResults[0].builder, ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        try
        {
            title = title.TrimEager().Truncate(40);
            var requestBuilder = RequestBuilder.Start().SetSearch(title);
            await DoRequestAndRespondAsync(ctx, ephemeral, requestBuilder).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to get compat list info");
        }
    }

    private static async ValueTask DoRequestAndRespondAsync(SlashCommandContext ctx, bool ephemeral, RequestBuilder requestBuilder)
    {
        Config.Log.Info(requestBuilder.Build());
        CompatResult? result = null;
        try
        {
            var remoteSearchTask = Client.GetCompatResultAsync(requestBuilder, Config.Cts.Token);
            var localResult = GetLocalCompatResult(requestBuilder);
            result = localResult;
            var remoteResult = await remoteSearchTask.ConfigureAwait(false);
            result = remoteResult?.Append(localResult);
        }
        catch
        {
            if (result == null)
            {
                await ctx.RespondAsync(embed: TitleInfo.CommunicationError.AsEmbed(null), ephemeral).ConfigureAwait(false);
                return;
            }
        }

#if DEBUG
        await Task.Delay(5_000).ConfigureAwait(false);
#endif
        if (result?.Results?.Count == 1)
        {
            var formattedResults = await ProductCodeLookup.LookupProductCodeAndFormatAsync(ctx.Client, [..result.Results.Keys]).ConfigureAwait(false);
            await ctx.RespondAsync(embed: formattedResults[0].builder, ephemeral: ephemeral).ConfigureAwait(false);
        }
        else if (result != null)
        {
            var builder = new StringBuilder();
            foreach (var msg in FormatSearchResults(ctx, result))
                builder.AppendLine(msg);
            var formattedResults = AutosplitResponseHelper.AutosplitMessage(builder.ToString(), blockStart: null, blockEnd: null);
            await ctx.RespondAsync(formattedResults[0], ephemeral).ConfigureAwait(false);
        }
    }

    internal static CompatResult GetLocalCompatResult(RequestBuilder requestBuilder)
    {
        var timer = Stopwatch.StartNew();
        var title = requestBuilder.Search;
        using var db = ThumbnailDb.OpenRead();
        var matches = db.Thumbnail
            .AsNoTracking()
            .AsEnumerable()
            .Select(t => (thumb: t, coef: title.GetFuzzyCoefficientCached(t.Name)))
            .OrderByDescending(i => i.coef)
            .Take(requestBuilder.AmountRequested)
            .ToList();
        var result = new CompatResult
        {
            RequestBuilder = requestBuilder,
            ReturnCode = 0,
            SearchTerm = requestBuilder.Search,
            Results = matches.ToDictionary(i => i.thumb.ProductCode, i => new TitleInfo
            {
                Status = i.thumb.CompatibilityStatus?.ToString() ?? "Unknown",
                Title = i.thumb.Name,
                Date = i.thumb.CompatibilityChangeDate?.AsUtc().ToString("yyyy-MM-dd"),
            })
        };
        timer.Stop();
        Config.Log.Debug($"Local compat list search time: {timer.ElapsedMilliseconds} ms");
        return result;
    }

    private static IEnumerable<string> FormatSearchResults(SlashCommandContext ctx, CompatResult compatResult)
    {
        var returnCode = ApiConfig.ReturnCodes[compatResult.ReturnCode];
        var request = compatResult.RequestBuilder;

        if (returnCode.overrideAll)
            yield return string.Format(returnCode.info, ctx.User.Mention);
        else
        {
            var authorMention = ctx.Channel.IsPrivate ? "You" : ctx.User.Mention;
            var result = new StringBuilder();
            result.AppendLine($"{authorMention} searched for: ***{request.Search?.Sanitize(replaceBackTicks: true)}***");
            if (request.Search?.Contains("persona", StringComparison.InvariantCultureIgnoreCase) is true
                || request.Search?.Contains("p5", StringComparison.InvariantCultureIgnoreCase) is true)
                result.AppendLine("Did you try searching for **__Unnamed__** instead?");
            result.AppendFormat(returnCode.info, compatResult.SearchTerm);
            yield return result.ToString();

            result.Clear();

            if (!returnCode.displayResults)
                yield break;
            
            var sortedList = compatResult.GetSortedList();
            var trimmedList = sortedList.Where(i => i.score > 0).ToList();
            if (trimmedList.Count > 0)
                sortedList = trimmedList;

            var searchTerm = request.Search ?? @"¯\\\_(ツ)\_/¯";
            var searchHits = sortedList.Where(t => t.score > 0.5
                                                   || (t.info.Title?.StartsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase) ?? false)
                                                   || (t.info.AlternativeTitle?.StartsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase) ?? false));
            foreach (var title in searchHits.Select(t => t.info.Title).Distinct())
                StatsStorage.IncGameStat(title);
            foreach (var resultInfo in sortedList.Take(request.AmountRequested))
            {
                var info = resultInfo.AsString();
#if DEBUG
                info = $"{StringUtils.InvisibleSpacer}`{CompatApiResultUtils.GetScore(request.Search, resultInfo.info):0.000000}` {info}";
#endif
                result.AppendLine(info);
            }
            yield return result.ToString();
        }
    }

    public static string FixGameTitleSearch(string title)
    {
        title = title.Trim(80);
        if (title.Equals("persona 5", StringComparison.InvariantCultureIgnoreCase)
            || title.Equals("p5", StringComparison.InvariantCultureIgnoreCase))
            title = "unnamed";
        else if (title.Equals("nnk", StringComparison.InvariantCultureIgnoreCase))
            title = "ni no kuni: wrath of the white witch";
        else if (title.Contains("mgs4", StringComparison.InvariantCultureIgnoreCase))
            title = title.Replace("mgs4", "mgs4gotp", StringComparison.InvariantCultureIgnoreCase);
        else if (title.Contains("metal gear solid 4", StringComparison.InvariantCultureIgnoreCase))
            title = title.Replace("metal gear solid 4", "mgs4gotp", StringComparison.InvariantCultureIgnoreCase);
        else if (title.Contains("lbp", StringComparison.InvariantCultureIgnoreCase))
            title = title.Replace("lbp", "littlebigplanet ", StringComparison.InvariantCultureIgnoreCase).TrimEnd();
        return title;
    }

    public static async Task ImportCompatListAsync()
    {
        var list = await Client.GetCompatListSnapshotAsync(Config.Cts.Token).ConfigureAwait(false);
        if (list is null)
            return;
            
        await using var db = ThumbnailDb.OpenRead();
        foreach (var kvp in list.Results)
        {
            var (productCode, info) = kvp;
            var dbItem = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productCode).ConfigureAwait(false);
            if (dbItem is null
                && await Client.GetCompatResultAsync(RequestBuilder.Start().SetSearch(productCode), Config.Cts.Token).ConfigureAwait(false) is {} compatItemSearchResult
                && compatItemSearchResult.Results.TryGetValue(productCode, out var compatItem))
            {
                dbItem = (await db.Thumbnail.AddAsync(new()
                {
                    ProductCode = productCode,
                    Name = compatItem.Title,
                }).ConfigureAwait(false)).Entity;
            }
            if (dbItem is null)
            {
                Config.Log.Debug($"Missing product code {productCode} in {nameof(ThumbnailDb)}");
                dbItem = new();
            }
            if (Enum.TryParse(info.Status, out CompatStatus status))
            {
                dbItem.CompatibilityStatus = status;
                if (info.Date is string d
                    && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                    dbItem.CompatibilityChangeDate = date.Ticks;
            }
            else
                Config.Log.Debug($"Failed to parse game compatibility status {info.Status}");
        }
        await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
    }

    public static async ValueTask ImportMetacriticScoresAsync()
    {
        var scoreJson = "metacritic_ps3.json";
        string json;
        if (File.Exists(scoreJson))
            json = await File.ReadAllTextAsync(scoreJson).ConfigureAwait(false);
        else
        {
            Config.Log.Warn($"Missing {scoreJson}, trying to get an online copy…");
            using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
            try
            {
                json = await httpClient.GetStringAsync($"https://raw.githubusercontent.com/RPCS3/discord-bot/master/{scoreJson}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to get online copy of {scoreJson}");
                return;
            }
        }

        var scoreList = JsonSerializer.Deserialize<List<Metacritic>>(json) ?? [];
            
        Config.Log.Debug($"Importing {scoreList.Count} Metacritic items");
        var duplicates = new List<Metacritic>();
        duplicates.AddRange(
            scoreList.Where(i => i.Title.StartsWith("Disney") || i.Title.StartsWith("DreamWorks") || i.Title.StartsWith("PlayStation"))
                .Select(i => i.WithTitle(i.Title.Split(' ', 2)[1]))
        );
        duplicates.AddRange(
            scoreList.Where(i => i.Title.Contains("A Telltale Game"))
                .Select(i => i.WithTitle(i.Title.Substring(0, i.Title.IndexOf("A Telltale Game", StringComparison.Ordinal) - 1).TrimEnd(' ', '-', ':')))
        );
        duplicates.AddRange(
            scoreList.Where(i => i.Title.StartsWith("Ratchet & Clank Future"))
                .Select(i => i.WithTitle(i.Title.Replace("Ratchet & Clank Future", "Ratchet & Clank")))
        );
        duplicates.AddRange(
            scoreList.Where(i => i.Title.StartsWith("MLB "))
                .Select(i => i.WithTitle($"Major League Baseball {i.Title[4..]}"))
        );
        duplicates.AddRange(
            scoreList.Where(i => i.Title.Contains("HAWX"))
                .Select(i => i.WithTitle(i.Title.Replace("HAWX", "H.A.W.X")))
        );

        await using var db = ThumbnailDb.OpenRead();
        foreach (var mcScore in scoreList.Where(s => s.CriticScore > 0 || s.UserScore > 0))
        {
            if (Config.Cts.IsCancellationRequested)
                return;

            var item = db.Metacritic.FirstOrDefault(i => i.Title == mcScore.Title);
            if (item == null)
                item = (await db.Metacritic.AddAsync(mcScore).ConfigureAwait(false)).Entity;
            else
            {
                item.CriticScore = mcScore.CriticScore;
                item.UserScore = mcScore.UserScore;
                item.Notes = mcScore.Notes;
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
                
            var title = mcScore.Title;
            var matches = db.Thumbnail
                //.Where(t => t.MetacriticId == null)
                .AsEnumerable()
                .Select(t => (thumb: t, coef: t.Name.GetFuzzyCoefficientCached(title)))
                .Where(i => i.coef > 0.90)
                .OrderByDescending(i => i.coef)
                .ToList();

            if (Config.Cts.IsCancellationRequested)
                return;

            if (matches.Any(m => m.coef > 0.99))
                matches = matches.Where(m => m.coef > 0.99).ToList();
            else if (matches.Any(m => m.coef > 0.95))
                matches = matches.Where(m => m.coef > 0.95).ToList();

            if (matches.Count == 0)
            {
                try
                {
                    var searchResult = await Client.GetCompatResultAsync(RequestBuilder.Start().SetSearch(title), Config.Cts.Token).ConfigureAwait(false);
                    var compatListMatches = searchResult?.Results
                                                .Select(i => (productCode: i.Key, titleInfo: i.Value, coef: Math.Max(title.GetFuzzyCoefficientCached(i.Value.Title), title.GetFuzzyCoefficientCached(i.Value.AlternativeTitle))))
                                                .Where(i => i.coef > 0.85)
                                                .OrderByDescending(i => i.coef)
                                                .ToList()
                                            ?? [];
                    if (compatListMatches.Any(i => i.coef > 0.99))
                        compatListMatches = compatListMatches.Where(i => i.coef > 0.99).ToList();
                    else if (compatListMatches.Any(i => i.coef > 0.95))
                        compatListMatches = compatListMatches.Where(i => i.coef > 0.95).ToList();
                    else if (compatListMatches.Any(i => i.coef > 0.90))
                        compatListMatches = compatListMatches.Where(i => i.coef > 0.90).ToList();
                    foreach ((string productCode, TitleInfo titleInfo, double coef) in compatListMatches)
                    {
                        var dbItem = await db.Thumbnail.FirstOrDefaultAsync(i => i.ProductCode == productCode).ConfigureAwait(false);
                        if (dbItem is null)
                            dbItem = (await db.Thumbnail.AddAsync(new()
                            {
                                ProductCode = productCode,
                                Name = titleInfo.Title,
                            }).ConfigureAwait(false)).Entity;
                            
                        dbItem.Name = titleInfo.Title;
                        if (Enum.TryParse(titleInfo.Status, out CompatStatus status))
                            dbItem.CompatibilityStatus = status;
                        if (DateTime.TryParseExact(titleInfo.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                            dbItem.CompatibilityChangeDate = date.Ticks;
                        matches.Add((dbItem, coef));
                    }
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e);
                }
            }
            matches = matches.Where(i => !TrialNamePattern().IsMatch(i.thumb.Name ?? "")).ToList();
            //var bestMatch = matches.FirstOrDefault();
            //Config.Log.Trace($"Best title match for [{item.Title}] is [{bestMatch.thumb.Name}] with score {bestMatch.coef:0.0000}");
            if (matches.Count > 0)
            {
                Config.Log.Trace($"Matched metacritic [{item.Title}] to compat titles: {string.Join(", ", matches.Select(m => $"[{m.thumb.Name}]"))}");
                foreach (var (thumb, _) in matches)
                    thumb.Metacritic = item;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            else
            {
                Config.Log.Warn($"Failed to find a single match for metacritic [{item.Title}]");
            }
        }
    }
}
