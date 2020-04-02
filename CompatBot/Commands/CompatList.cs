using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.TeamFoundation.Build.WebApi;
using Newtonsoft.Json;

namespace CompatBot.Commands
{
    internal sealed class CompatList : BaseCommandModuleCustom
    {
        private static readonly Client client = new Client();
        private static readonly GithubClient.Client githubClient = new GithubClient.Client();
        private static readonly SemaphoreSlim updateCheck = new SemaphoreSlim(1, 1);
        private static string lastUpdateInfo = null;
        private const string Rpcs3UpdateStateKey = "Rpcs3UpdateState";
        private static UpdateInfo CachedUpdateInfo = null;

        static CompatList()
        {
            using var db = new BotDb();
            lastUpdateInfo = db.BotState.FirstOrDefault(k => k.Key == Rpcs3UpdateStateKey)?.Value;
            if (lastUpdateInfo is string strPr
                && int.TryParse(strPr, out var pr))
            {
                try
                {
                    var prInfo = githubClient.GetPrInfoAsync(pr, Config.Cts.Token).GetAwaiter().GetResult();
                    CachedUpdateInfo = client.GetUpdateAsync(Config.Cts.Token, prInfo?.MergeCommitSha).GetAwaiter().GetResult();
                    if (CachedUpdateInfo.CurrentBuild != null)
                    {
                        CachedUpdateInfo.LatestBuild = CachedUpdateInfo.CurrentBuild;
                        CachedUpdateInfo.CurrentBuild = null;
                    }
                }
                catch { }
            }
        }

        [Command("compat"), Aliases("c", "compatibility")]
        [Description("Searches the compatibility database, USE: !compat search term")]
        public async Task Compat(CommandContext ctx, [RemainingText, Description("Game title to look up")] string title)
        {
            title = title?.TrimEager().Truncate(40);
            if (string.IsNullOrEmpty(title))
            {
                var prompt = await ctx.RespondAsync($"{ctx.Message.Author.Mention} what game do you want to check?").ConfigureAwait(false);
                var interact = ctx.Client.GetInteractivity();
                var response = await interact.WaitForMessageAsync(m => m.Author == ctx.Message.Author && m.Channel == ctx.Channel).ConfigureAwait(false);
                if (string.IsNullOrEmpty(response.Result?.Content) || response.Result.Content.StartsWith(Config.CommandPrefix))
                {
                    await prompt.ModifyAsync("You should specify what you're looking for").ConfigureAwait(false);
                    return;
                }

                await prompt.DeleteAsync().ConfigureAwait(false);
                title = response.Result.Content.TrimEager().Truncate(40);
            }

            if (!await DiscordInviteFilter.CheckMessageForInvitesAsync(ctx.Client, ctx.Message).ConfigureAwait(false))
                return;

            if (!await ContentFilter.IsClean(ctx.Client, ctx.Message).ConfigureAwait(false))
                return;

            var productCodes = ProductCodeLookup.GetProductIds(ctx.Message.Content);
            if (productCodes.Any())
            {
                await ProductCodeLookup.LookupAndPostProductCodeEmbedAsync(ctx.Client, ctx.Message, productCodes).ConfigureAwait(false);
                return;
            }

            try
            {
                var requestBuilder = RequestBuilder.Start().SetSearch(title);
                await DoRequestAndRespond(ctx, requestBuilder).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to get compat list info");
            }
        }

        [Group("latest"), TriggersTyping]
        [Description("Provides links to the latest RPCS3 build")]
        [Cooldown(1, 30, CooldownBucketType.Channel)]
        public sealed class UpdatesCheck: BaseCommandModuleCustom
        {
            [GroupCommand]
            public Task Latest(CommandContext ctx)
            {
                return CheckForRpcs3Updates(ctx.Client, ctx.Channel);
            }

            [Command("since")]
            [Description("Show additional info about changes since specified update")]
            public Task Since(CommandContext ctx, [Description("Commit hash of the update, such as `46abe0f31`")] string commit)
            {
                return CheckForRpcs3Updates(ctx.Client, ctx.Channel, commit);
            }

            [Command("clear"), RequiresBotModRole]
            [Description("Clears update info cache that is used to post new build announcements")]
            public Task Clear(CommandContext ctx)
            {
                lastUpdateInfo = null;
                return CheckForRpcs3Updates(ctx.Client, null);
            }

            [Command("restore"), RequiresBotModRole]
            [Description("Regenerates update announcement for specified bot message and build hash")]
            public async Task Restore(CommandContext ctx, string botMsgLink, string updateCommitHash)
            {
                var botMsg = await ctx.GetMessageAsync(botMsgLink).ConfigureAwait(false);
                if (botMsg?.Author == null || !botMsg.Author.IsCurrent || !string.IsNullOrEmpty(botMsg.Content) || botMsg.Embeds.Any())
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid source message link").ConfigureAwait(false);
                    return;
                }

                if (!await CheckForRpcs3Updates(ctx.Client, null, updateCommitHash, botMsg).ConfigureAwait(false))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to fetch update info").ConfigureAwait(false);
                    return;
                }

                await ctx.ReactWithAsync(Config.Reactions.Success).ConfigureAwait(false);
            }

            public static async Task<bool> CheckForRpcs3Updates(DiscordClient discordClient, DiscordChannel channel, string sinceCommit = null, DiscordMessage emptyBotMsg = null)
            {
                var updateAnnouncement = channel is null;
                var updateAnnouncementRestore = emptyBotMsg != null;
                var info = await client.GetUpdateAsync(Config.Cts.Token, sinceCommit).ConfigureAwait(false);
                if (info?.ReturnCode != 1 && sinceCommit != null)
                    info = await client.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);

                if (updateAnnouncementRestore && info?.CurrentBuild != null)
                    info.LatestBuild = info.CurrentBuild;
                var embed = await info.AsEmbedAsync(discordClient, updateAnnouncement).ConfigureAwait(false);
                if (info == null || embed.Color.Value.Value == Config.Colors.Maintenance.Value)
                {
                    if (updateAnnouncementRestore)
                    {
                        Config.Log.Debug($"Failed to get update info for commit {sinceCommit}: {JsonConvert.SerializeObject(info)}");
                        return false;
                    }

                    embed = await CachedUpdateInfo.AsEmbedAsync(discordClient, updateAnnouncement).ConfigureAwait(false);
                }
                else if (!updateAnnouncementRestore)
                {
                    if (CachedUpdateInfo.LatestBuild?.Datetime is string previousBuildTimeStr
                        && info.LatestBuild?.Datetime is string newBuildTimeStr
                        && DateTime.TryParse(previousBuildTimeStr, out var previousBuildTime)
                        && DateTime.TryParse(newBuildTimeStr, out var newBuildTime)
                        && newBuildTime > previousBuildTime)
                        CachedUpdateInfo = info;
                    else
                        return true;
                }
                if (!updateAnnouncement)
                    await channel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                else if (updateAnnouncementRestore)
                {
                    if (embed.Title == "Error")
                        return false;

                    Config.Log.Debug($"Restoring update announcement for build {sinceCommit}: {embed.Title}\n{embed.Description}");
                    await emptyBotMsg.ModifyAsync(embed: embed.Build()).ConfigureAwait(false);
                    return true;
                }

                var latestUpdatePr = info?.LatestBuild?.Pr?.ToString();
                if (string.IsNullOrEmpty(latestUpdatePr)
                    || lastUpdateInfo == latestUpdatePr
                    || !await updateCheck.WaitAsync(0).ConfigureAwait(false))
                    return false;

                try
                {
                    var compatChannel = await discordClient.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
                    var botMember = discordClient.GetMember(compatChannel.Guild, discordClient.CurrentUser);
                    if (botMember == null)
                        return false;

                    if (!compatChannel.PermissionsFor(botMember).HasPermission(Permissions.SendMessages))
                    {
                        NewBuildsMonitor.Reset();
                        return false;
                    }

                    if (!updateAnnouncement)
                        embed = await CachedUpdateInfo.AsEmbedAsync(discordClient, true).ConfigureAwait(false);
                    if (embed.Color.Value.Value == Config.Colors.Maintenance.Value)
                        return false;

                    await CheckMissedBuildsBetween(discordClient, compatChannel, lastUpdateInfo, latestUpdatePr, Config.Cts.Token).ConfigureAwait(false);

                    await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                    lastUpdateInfo = latestUpdatePr;
                    using var db = new BotDb();
                    var currentState = await db.BotState.FirstOrDefaultAsync(k => k.Key == Rpcs3UpdateStateKey).ConfigureAwait(false);
                    if (currentState == null)
                        db.BotState.Add(new BotState {Key = Rpcs3UpdateStateKey, Value = latestUpdatePr});
                    else
                        currentState.Value = latestUpdatePr;
                    await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                    NewBuildsMonitor.Reset();
                    return true;
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to check for RPCS3 update info");
                }
                finally
                {
                    updateCheck.Release();
                }
                return false;
            }

            private static async Task CheckMissedBuildsBetween(DiscordClient discordClient, DiscordChannel compatChannel, string previousUpdatePr, string latestUpdatePr, CancellationToken cancellationToken)
            {
                if (!int.TryParse(previousUpdatePr, out var oldestPr)
                    || !int.TryParse(latestUpdatePr, out var newestPr))
                    return;

                var mergedPrs = await githubClient.GetClosedPrsAsync(cancellationToken).ConfigureAwait(false); // this will cache 30 latest PRs
                var newestPrCommit = await githubClient.GetPrInfoAsync(newestPr, cancellationToken).ConfigureAwait(false);
                var oldestPrCommit = await githubClient.GetPrInfoAsync(oldestPr, cancellationToken).ConfigureAwait(false);
                if (newestPrCommit.MergedAt == null || oldestPrCommit.MergedAt == null)
                    return;

                mergedPrs = mergedPrs.Where(pri => pri.MergedAt.HasValue)
                    .OrderBy(pri => pri.MergedAt.Value)
                    .SkipWhile(pri => pri.Number != oldestPr)
                    .Skip(1)
                    .TakeWhile(pri => pri.Number != newestPr)
                    .ToList();
                if (mergedPrs.Count == 0)
                    return;

                var failedBuilds = await Config.GetAzureDevOpsClient().GetMasterBuildsAsync(
                    oldestPrCommit.MergeCommitSha,
                    newestPrCommit.MergeCommitSha,
                    oldestPrCommit.MergedAt,
                    cancellationToken
                ).ConfigureAwait(false);
                foreach (var mergedPr in mergedPrs)
                {
                    var updateInfo = await client.GetUpdateAsync(cancellationToken, mergedPr.MergeCommitSha).ConfigureAwait(false);
                    if (updateInfo.ReturnCode == 0 || updateInfo.ReturnCode == 1) // latest or known build
                    {
                        updateInfo.LatestBuild = updateInfo.CurrentBuild;
                        updateInfo.CurrentBuild = null;
                        var embed = await updateInfo.AsEmbedAsync(discordClient, true).ConfigureAwait(false);
                        await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                    }
                    else if (updateInfo.ReturnCode == -1) // unknown build
                    {
                        var masterBuildInfo = failedBuilds.FirstOrDefault(b => b.Commit.Equals(mergedPr.MergeCommitSha, StringComparison.InvariantCultureIgnoreCase));
                        if (masterBuildInfo == null)
                            continue;

                        var buildTime = masterBuildInfo.FinishTime;
                        updateInfo = new UpdateInfo
                        {
                            ReturnCode = 1,
                            LatestBuild = new BuildInfo
                            {
                                Datetime = buildTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                                Pr = mergedPr.Number,
                                Windows = new BuildLink
                                {
                                    Download = masterBuildInfo.WindowsBuildDownloadLink,
                                },
                                Linux = new BuildLink
                                {
                                    Download = masterBuildInfo.LinuxBuildDownloadLink,
                                },
                            },
                        };
                        var embed = await updateInfo.AsEmbedAsync(discordClient, true).ConfigureAwait(false);
                        embed.Color = Config.Colors.PrClosed;
                        embed.ClearFields();
                        var reason = masterBuildInfo.Result switch
                        {
                            BuildResult.Succeeded => "Built",
                            BuildResult.PartiallySucceeded => "Built",
                            BuildResult.Failed => "Failed to build",
                            BuildResult.Canceled => "Cancelled",
                            _ => null,
                        };
                        if (buildTime.HasValue && reason != null)
                            embed.WithFooter($"{reason} on {buildTime:u} ({(DateTime.UtcNow - buildTime.Value).AsTimeDeltaDescription()} ago)");
                        else
                            embed.WithFooter(reason);
                        await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task DoRequestAndRespond(CommandContext ctx, RequestBuilder requestBuilder)
        {
            Config.Log.Info(requestBuilder.Build());
            CompatResult result = null;
            try
            {
                var remoteSearchTask = client.GetCompatResultAsync(requestBuilder, Config.Cts.Token);
                var localResult = GetLocalCompatResult(requestBuilder);
                result = localResult;
                var remoteResult = await remoteSearchTask.ConfigureAwait(false);
                if (remoteResult.Results?.Count > 0)
                    foreach (var localItem in localResult.Results)
                    {
                        if (remoteResult.Results.ContainsKey(localItem.Key))
                            continue;

                        remoteResult.Results[localItem.Key] = localItem.Value;
                    }
                result = remoteResult;
            }
            catch
            {
                if (result == null)
                {
                    await ctx.RespondAsync(embed: TitleInfo.CommunicationError.AsEmbed(null)).ConfigureAwait(false);
                    return;
                }
            }

#if DEBUG
            await Task.Delay(5_000).ConfigureAwait(false);
#endif
            var channel = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            if (result.Results?.Count == 1)
                await ProductCodeLookup.LookupAndPostProductCodeEmbedAsync(ctx.Client, ctx.Message, new List<string>(result.Results.Keys)).ConfigureAwait(false);
            else
                foreach (var msg in FormatSearchResults(ctx, result))
                    await channel.SendAutosplitMessageAsync(msg, blockStart: "", blockEnd: "").ConfigureAwait(false);
        }

        private CompatResult GetLocalCompatResult(RequestBuilder requestBuilder)
        {
            var timer = Stopwatch.StartNew();
            var title = requestBuilder.search;
            using var db = new ThumbnailDb();
            var matches = db.Thumbnail
                .AsNoTracking()
                .AsEnumerable()
                .Select(t => (thumb: t, coef: title.GetFuzzyCoefficientCached(t.Name)))
                .OrderByDescending(i => i.coef)
                .Take(requestBuilder.amountRequested)
                .ToList();
            var result = new CompatResult
            {
                RequestBuilder = requestBuilder,
                ReturnCode = 0,
                SearchTerm = requestBuilder.search,
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

        private IEnumerable<string> FormatSearchResults(CommandContext ctx, CompatResult compatResult)
        {
            var returnCode = ApiConfig.ReturnCodes[compatResult.ReturnCode];
            var request = compatResult.RequestBuilder;

            if (returnCode.overrideAll)
                yield return string.Format(returnCode.info, ctx.Message.Author.Mention);
            else
            {
                var authorMention = ctx.Channel.IsPrivate ? "You" : ctx.Message.Author.Mention;
                var result = new StringBuilder();
                if (string.IsNullOrEmpty(request.customHeader))
                {
                    result.AppendLine($"{authorMention} searched for: ***{request.search.Sanitize(replaceBackTicks: true)}***");
                    if (request.search.Contains("persona", StringComparison.InvariantCultureIgnoreCase)
                        || request.search.Contains("p5", StringComparison.InvariantCultureIgnoreCase))
                        result.AppendLine("Did you try searching for **__Unnamed__** instead?");
                    else if (!ctx.Channel.IsPrivate
                             && ctx.Message.Author.Id == 197163728867688448
                             && (compatResult.Results.Values.Any(i =>
                                 i.Title.Contains("afrika", StringComparison.InvariantCultureIgnoreCase)
                                 || i.Title.Contains("africa", StringComparison.InvariantCultureIgnoreCase)))
                    )
                    {
                        var sqvat = ctx.Client.GetEmoji(":sqvat:", Config.Reactions.No);
                        result.AppendLine($"One day this meme will die {sqvat}");
                    }
                }
                else
                {
                    var formattedHeader = string.Format(request.customHeader, authorMention, request.amountRequested, null, null);
                    result.AppendLine(formattedHeader.Replace("   ", " ").Replace("  ", " "));
                }
                result.AppendFormat(returnCode.info, compatResult.SearchTerm);
                yield return result.ToString();

                result.Clear();

                if (returnCode.displayResults)
                {
                    var sortedList = compatResult.GetSortedList();
                    var searchTerm = request.search ?? @"¯\_(ツ)_/¯";
                    var searchHits = sortedList.Where(t => t.score > 0.5
                                                           || (t.info.Title?.StartsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase) ?? false)
                                                           || (t.info.AlternativeTitle?.StartsWith(searchTerm, StringComparison.InvariantCultureIgnoreCase) ?? false));
                    foreach (var title in searchHits.Select(t => t.info?.Title).Distinct())
                    {
                        StatsStorage.GameStatCache.TryGetValue(title, out int stat);
                        StatsStorage.GameStatCache.Set(title, ++stat, StatsStorage.CacheTime);
                    }
                    foreach (var resultInfo in sortedList.Take(request.amountRequested))
                    {
                        var info = resultInfo.AsString();
#if DEBUG
                        info = $"{StringUtils.InvisibleSpacer}`{CompatApiResultUtils.GetScore(request.search, resultInfo.info):0.000000}` {info}";
#endif
                        result.AppendLine(info);
                    }
                    yield return result.ToString();
                }
            }
        }

        public static string FixGameTitleSearch(string title)
        {
            title = title.Trim(40);
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
            var list = await client.GetCompatListSnapshotAsync(Config.Cts.Token).ConfigureAwait(false);
            using var db = new ThumbnailDb();
            foreach (var kvp in list.Results)
            {
                var (productCode, info) = kvp;
                var dbItem = await db.Thumbnail.FirstOrDefaultAsync(t => t.ProductCode == productCode).ConfigureAwait(false);
                if (dbItem == null)
                {
                    var compatItemSearchResult = await client.GetCompatResultAsync(RequestBuilder.Start().SetSearch(productCode), Config.Cts.Token).ConfigureAwait(false);
                    if (compatItemSearchResult.Results.TryGetValue(productCode, out var compatItem))
                        dbItem = db.Thumbnail.Add(new Thumbnail
                        {
                            ProductCode = productCode,
                            Name = compatItem.Title,
                        }).Entity;
                }
                if (dbItem == null)
                    Config.Log.Debug($"Missing product code {productCode} in {nameof(ThumbnailDb)}");
                if (Enum.TryParse(info.Status, out CompatStatus status))
                {
                    dbItem.CompatibilityStatus = status;
                    if (info.Date is string d && DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                        dbItem.CompatibilityChangeDate = date.Ticks;
                }
                else
                    Config.Log.Debug($"Failed to parse game compatibility status {info.Status}");
            }
            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }
    }
}