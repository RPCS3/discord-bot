using System.Text.Json;
using CompatApiClient.POCOs;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus.Commands.Processors.TextCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.TeamFoundation.Build.WebApi;

namespace CompatBot.Commands;

internal static partial class CompatList
{
    [Command("latest"), AllowDMUsage]
    public static class LatestBuild
    {
        [Command("build"), AllowDMUsage]
        [Description("Link to the latest RPCS3 build")]
        public static ValueTask Latest(SlashCommandContext ctx) => CheckForRpcs3UpdatesAsync(ctx, respond: true);

        /*
        [Command("since")]
        [Description("Show additional info about changes since specified update")]
        public static ValueTask Since(TextCommandContext ctx, [Description("Commit hash of the update, such as `46abe0f31`")] string commit)
            => CheckForRpcs3UpdatesAsync(ctx, respond: true, sinceCommit: commit);
        */

        [Command("clear"), RequiresBotModRole]
        [Description("Clear the update info cache and post the latest RPCS3 build announcement")]
        public static ValueTask Clear(TextCommandContext ctx)
        {
            lastUpdateInfo = null;
            lastFullBuildNumber = null;
            return CheckForRpcs3UpdatesAsync(ctx);
        }

        [Command("set"), RequiresBotModRole]
        [Description("Set the latest build info to the specified PR merge, and post new release announcements after it")]
        public static ValueTask Set(TextCommandContext ctx, string lastUpdatePr)
        {
            lastUpdateInfo = lastUpdatePr;
            lastFullBuildNumber = null;
            return CheckForRpcs3UpdatesAsync(ctx);
        }

        public static async ValueTask CheckForRpcs3UpdatesAsync(
            CommandContext? ctx = null,
            DiscordClient? discordClient = null,
            bool respond = false,
            string? sinceCommit = null,
            DiscordMessage? emptyBotMsg = null
        )
        {
            if (ctx is null && respond)
                throw new InvalidOperationException($"{nameof(Latest)}: request to respond without ctx");

            discordClient ??= ctx?.Client;
            if (discordClient is null)
                throw new InvalidOperationException($"{nameof(Latest)}: no discord client provided");

            var ephemeral = true;
            if (respond)
            {
                ephemeral = !ctx!.Channel.IsSpamChannel();
                if (ctx is SlashCommandContext sctx)
                    await sctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
            }

            var updateAnnouncementRestore = emptyBotMsg is not null;
            var info = await Client.GetUpdateAsync(Config.Cts.Token, sinceCommit).ConfigureAwait(false);
            if (info?.ReturnCode != 1 && sinceCommit != null)
                info = await Client.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);

            if (updateAnnouncementRestore && info?.CurrentBuild != null)
                info.LatestBuild = info.CurrentBuild;
            var embed = await info.AsEmbedAsync(discordClient, !respond).ConfigureAwait(false);
            if (info is null || embed.Color!.Value.Value == Config.Colors.Maintenance.Value)
            {
                if (updateAnnouncementRestore)
                {
                    Config.Log.Debug($"Failed to get update info for commit {sinceCommit}: {JsonSerializer.Serialize(info)}");
                    return;
                }

                embed = await cachedUpdateInfo.AsEmbedAsync(discordClient, !respond).ConfigureAwait(false);
            }
            else if (!updateAnnouncementRestore)
            {
                if (cachedUpdateInfo?.LatestBuild?.Datetime is string previousBuildTimeStr
                    && info.LatestBuild?.Datetime is string newBuildTimeStr
                    && DateTime.TryParse(previousBuildTimeStr, out var previousBuildTime)
                    && DateTime.TryParse(newBuildTimeStr, out var newBuildTime)
                    && newBuildTime > previousBuildTime)
                    cachedUpdateInfo = info;
            }
            if (respond)
            {
                if (ctx is SlashCommandContext sctx)
                    await sctx.RespondAsync(embed: embed.Build(), ephemeral: ephemeral).ConfigureAwait(false);
                else
                    await ctx!.RespondAsync(embed: embed.Build()).ConfigureAwait(false);
                return;
            }
                
            if (updateAnnouncementRestore)
            {
                if (embed.Title is "Error")
                    return;

                Config.Log.Debug($"Restoring update announcement for build {sinceCommit}: {embed.Title}\n{embed.Description}");
                await emptyBotMsg!.ModifyAsync(embed: embed.Build()).ConfigureAwait(false);
                return;
            }

            var latestUpdatePr = info?.LatestBuild?.Pr?.ToString();
            var match = (
                from field in embed.Fields
                let m = UpdateVersionRegex().Match(field.Value)
                where m.Success
                select m
            ).FirstOrDefault();
            var latestUpdateBuild = match?.Groups["build"].Value;

            if (string.IsNullOrEmpty(latestUpdatePr)
                || lastUpdateInfo == latestUpdatePr
                || !await UpdateCheck.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                if (!string.IsNullOrEmpty(lastFullBuildNumber)
                    && !string.IsNullOrEmpty(latestUpdateBuild)
                    && int.TryParse(lastFullBuildNumber, out var lastSaveBuild)
                    && int.TryParse(latestUpdateBuild, out var latestBuild)
                    && latestBuild <= lastSaveBuild)
                    return;

                var compatChannel = await discordClient.GetChannelAsync(Config.BotChannelId).ConfigureAwait(false);
                var botMember = await discordClient.GetMemberAsync(compatChannel.Guild, discordClient.CurrentUser).ConfigureAwait(false);
                if (botMember is null)
                    return;

                if (!compatChannel.PermissionsFor(botMember).HasPermission(DiscordPermission.SendMessages))
                {
                    NewBuildsMonitor.Reset();
                    return;
                }

                if (embed.Color!.Value.Value == Config.Colors.Maintenance.Value)
                    return;

                await CheckMissedBuildsBetweenAsync(discordClient, compatChannel, lastUpdateInfo, latestUpdatePr, Config.Cts.Token).ConfigureAwait(false);

                await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                lastUpdateInfo = latestUpdatePr;
                lastFullBuildNumber = latestUpdateBuild;
                await using var db = new BotDb();
                var currentState = await db.BotState.FirstOrDefaultAsync(k => k.Key == Rpcs3UpdateStateKey).ConfigureAwait(false);
                if (currentState == null)
                    await db.BotState.AddAsync(new() {Key = Rpcs3UpdateStateKey, Value = latestUpdatePr}).ConfigureAwait(false);
                else
                    currentState.Value = latestUpdatePr;
                var savedLastBuild = await db.BotState.FirstOrDefaultAsync(k => k.Key == Rpcs3UpdateBuildKey).ConfigureAwait(false);
                if (savedLastBuild == null)
                    await db.BotState.AddAsync(new() {Key = Rpcs3UpdateBuildKey, Value = latestUpdateBuild}).ConfigureAwait(false);
                else
                    savedLastBuild.Value = latestUpdateBuild;
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                NewBuildsMonitor.Reset();
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to check for RPCS3 update info");
            }
            finally
            {
                UpdateCheck.Release();
            }
        }

        private static async ValueTask CheckMissedBuildsBetweenAsync(DiscordClient discordClient, DiscordChannel compatChannel, string? previousUpdatePr, string? latestUpdatePr, CancellationToken cancellationToken)
        {
            if (!int.TryParse(previousUpdatePr, out var oldestPr)
                || !int.TryParse(latestUpdatePr, out var newestPr))
                return;

            var mergedPrs = await GithubClient.GetClosedPrsAsync(cancellationToken).ConfigureAwait(false); // this will cache 30 latest PRs
            var newestPrCommit = await GithubClient.GetPrInfoAsync(newestPr, cancellationToken).ConfigureAwait(false);
            var oldestPrCommit = await GithubClient.GetPrInfoAsync(oldestPr, cancellationToken).ConfigureAwait(false);
            if (newestPrCommit?.MergedAt == null || oldestPrCommit?.MergedAt == null)
                return;

            mergedPrs = mergedPrs?.Where(pri => pri.MergedAt.HasValue)
                .OrderBy(pri => pri.MergedAt!.Value)
                .SkipWhile(pri => pri.Number != oldestPr)
                .Skip(1)
                .TakeWhile(pri => pri.Number != newestPr)
                .ToList();
            if (mergedPrs is null or {Count: 0})
                return;

            var failedBuilds = await Config.GetAzureDevOpsClient().GetMasterBuildsAsync(
                oldestPrCommit.MergeCommitSha,
                newestPrCommit.MergeCommitSha,
                oldestPrCommit.MergedAt?.DateTime,
                cancellationToken
            ).ConfigureAwait(false);
            foreach (var mergedPr in mergedPrs)
            {
                var updateInfo = await Client.GetUpdateAsync(cancellationToken, mergedPr.MergeCommitSha).ConfigureAwait(false)
                                 ?? new UpdateInfo {ReturnCode = -1};
                if (updateInfo.ReturnCode is 0 or 1) // latest or known build
                {
                    updateInfo.LatestBuild = updateInfo.CurrentBuild;
                    updateInfo.CurrentBuild = null;
                    var embed = await updateInfo.AsEmbedAsync(discordClient, true).ConfigureAwait(false);
                    await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                }
                else if (updateInfo.ReturnCode == -1) // unknown build
                {
                    var masterBuildInfo = failedBuilds?.FirstOrDefault(b => b.Commit?.Equals(mergedPr.MergeCommitSha, StringComparison.InvariantCultureIgnoreCase) is true);
                    var buildTime = masterBuildInfo?.FinishTime;
                    if (masterBuildInfo != null)
                    {
                        updateInfo = new()
                        {
                            ReturnCode = 1,
                            LatestBuild = new()
                            {
                                Datetime = buildTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                                Pr = mergedPr.Number,
                                Windows = new() {Download = masterBuildInfo.WindowsBuildDownloadLink ?? ""},
                                Linux = new() { Download = masterBuildInfo.LinuxBuildDownloadLink ?? "" },
                                Mac = new() { Download = masterBuildInfo.MacBuildDownloadLink ?? "" },
                            },
                        };
                    }
                    else
                    {
                        updateInfo = new()
                        {
                            ReturnCode = 1,
                            LatestBuild = new()
                            {
                                Pr = mergedPr.Number,
                                Windows = new() {Download = ""},
                                Linux = new() { Download = "" },
                                Mac = new() { Download = "" },
                            },
                        };
                    }
                    var embed = await updateInfo.AsEmbedAsync(discordClient, true).ConfigureAwait(false);
                    embed.Color = Config.Colors.PrClosed;
                    embed.ClearFields();
                    var reason = masterBuildInfo?.Result switch
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
                        embed.WithFooter(reason ?? "Never built");
                    await compatChannel.SendMessageAsync(embed: embed.Build()).ConfigureAwait(false);
                }
            }
        }
    }
}