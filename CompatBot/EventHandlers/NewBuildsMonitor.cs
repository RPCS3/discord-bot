using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers;

internal static class NewBuildsMonitor
{
    private static readonly Regex BuildResult = new(@"\[rpcs3:master\] \d+ new commit", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly TimeSpan PassiveCheckInterval = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ActiveCheckInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ActiveCheckResetThreshold = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentQueue<(DateTime start, DateTime end)> ExpectedNewBuildTimeFrames = new();

    public static async Task OnMessageCreated(DiscordClient _, MessageCreateEventArgs args)
    {
        if (args.Author.IsBotSafeCheck()
            && !args.Author.IsCurrent
            && "github".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase)
            && args.Message.Embeds.FirstOrDefault() is DiscordEmbed embed
            && !string.IsNullOrEmpty(embed.Title)
            && BuildResult.IsMatch(embed.Title)
           )
        {
            Config.Log.Info("Found new PR merge message");
            var azureClient = Config.GetAzureDevOpsClient();
            var pipelineDurationStats = await azureClient.GetPipelineDurationAsync(Config.Cts.Token).ConfigureAwait(false);
            var expectedMean = DateTime.UtcNow + pipelineDurationStats.Mean;
            var start = expectedMean - pipelineDurationStats.StdDev;
            var end = expectedMean + pipelineDurationStats.StdDev + ActiveCheckResetThreshold;
            ExpectedNewBuildTimeFrames.Enqueue((start, end));
        }
    }

    public static async Task MonitorAsync(DiscordClient client)
    {
        var lastCheck = DateTime.UtcNow.AddDays(-1);
        Exception? lastException = null;
        while (!Config.Cts.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var checkInterval = PassiveCheckInterval;
            (DateTime start, DateTime end) nearestBuildCheckInterval = default;
            while (!ExpectedNewBuildTimeFrames.IsEmpty
                   && ExpectedNewBuildTimeFrames.TryPeek(out nearestBuildCheckInterval)
                   && nearestBuildCheckInterval.end < now)
            {
                ExpectedNewBuildTimeFrames.TryDequeue(out _);
            }
            if (nearestBuildCheckInterval.start < now && now < nearestBuildCheckInterval.end)
                checkInterval = ActiveCheckInterval;
            if (lastCheck + checkInterval < now)
            {
                try
                {
                    await CompatList.UpdatesCheck.CheckForRpcs3Updates(client, null).ConfigureAwait(false);
                    lastCheck = DateTime.UtcNow;
                }
                catch (Exception e)
                {
                    if (e.GetType() != lastException?.GetType())
                    {
                        Config.Log.Debug(e);
                        lastException = e;
                    }
                }
            }
            await Task.Delay(1000, Config.Cts.Token).ConfigureAwait(false);
        }
    }

    internal static void Reset()
    {
        var now = DateTime.UtcNow;
        if (!ExpectedNewBuildTimeFrames.IsEmpty
            && ExpectedNewBuildTimeFrames.TryPeek(out var ebci)
            && ebci.start < now)
        {
            ExpectedNewBuildTimeFrames.TryDequeue(out _);
        }
    }
}