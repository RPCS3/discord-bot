using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using DSharpPlus;
using Microsoft.ApplicationInsights;
using NLog;

namespace CompatBot
{
    internal static class Watchdog
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
        public static readonly ConcurrentQueue<DateTime> DisconnectTimestamps = new ConcurrentQueue<DateTime>();
        public static readonly Stopwatch TimeSinceLastIncomingMessage = Stopwatch.StartNew();
        private static bool IsOk => DisconnectTimestamps.IsEmpty && TimeSinceLastIncomingMessage.Elapsed < Config.IncomingMessageCheckIntervalInMin;
        private static DiscordClient discordClient = null;

        public static async Task Watch(DiscordClient client)
        {
            discordClient = client;
            do
            {
                await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                foreach (var sudoer in ModProvider.Mods.Values.Where(m => m.Sudoer))
                {
                    var user = await client.GetUserAsync(sudoer.DiscordId).ConfigureAwait(false);
                    if (user?.Presence?.Activity?.CustomStatus?.Name is string cmd && cmd.StartsWith("restart"))
                    {
                        var instance = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                        if (ulong.TryParse(instance, out var botId) && botId == client.CurrentUser.Id)
                        {
                            Config.Log.Warn($"Found request to restart on {user.Username}#{user.Discriminator}'s custom status");
                            Sudo.Bot.Restart(Program.InvalidChannelId, $"Restarted by request from {user.Username}#{user.Discriminator}'s custom status");
                        }
                    }
                }

                if (IsOk)
                    continue;

                try
                {
                    Config.TelemetryClient?.TrackEvent("socket-deadlock-potential");
                    Config.Log.Warn("Potential socket deadlock detected, reconnecting...");
                    await client.ReconnectAsync(true).ConfigureAwait(false);
                    await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                    if (IsOk)
                    {
                        Config.Log.Info("Looks like we're back in business");
                        continue;
                    }

                    Config.TelemetryClient?.TrackEvent("socket-deadlock-for-sure");
                    Config.Log.Error("Hard reconnect failed, restarting...");
                    Sudo.Bot.Restart(Program.InvalidChannelId, $@"Restarted to reset potential socket deadlock (last incoming message event: {TimeSinceLastIncomingMessage.Elapsed:h\:mm\:ss} ago)");
                }
                catch (Exception e)
                {
                    Config.Log.Error(e);
                }
            } while (!Config.Cts.IsCancellationRequested);
        }

        public static void OnLogHandler(string level, string message)
        {
            if (level == nameof(LogLevel.Info))
            {
                if (message?.Contains("Session resumed") ?? false)
                    DisconnectTimestamps.Clear();
            }
            else if (level == nameof(LogLevel.Warn))
            {
                if (message?.Contains("Dispatch:PRESENCES_REPLACE") ?? false)
                    BotStatusMonitor.RefreshAsync(discordClient).ConfigureAwait(false).GetAwaiter().GetResult();
                else if (message?.Contains("Pre-emptive ratelimit triggered") ?? false)
                    Config.TelemetryClient?.TrackEvent("preemptive-rate-limit");
            }
            else if (level == nameof(LogLevel.Fatal))
            {
                if ((message?.Contains("Socket connection terminated") ?? false)
                    || (message?.Contains("heartbeats were skipped. Issuing reconnect.") ?? false))
                    DisconnectTimestamps.Enqueue(DateTime.UtcNow);
            }
        }

        public static async Task SendMetrics(DiscordClient client)
        {
            do
            {
                await Task.Delay(Config.MetricsIntervalInSec).ConfigureAwait(false);
                using var process = Process.GetCurrentProcess();
                if (Config.TelemetryClient is TelemetryClient tc)
                {
                    tc.TrackMetric("gw-latency", client.Ping);
                    tc.TrackMetric("time-since-last-incoming-message", TimeSinceLastIncomingMessage.ElapsedMilliseconds);
                    tc.TrackMetric("gc-total-memory", GC.GetTotalMemory(false));
                    tc.TrackMetric("process-total-memory", process.PrivateMemorySize64);
                    tc.TrackMetric("github-limit-remaining", GithubClient.Client.RateLimitRemaining);
                    tc.Flush();
                }
            } while (!Config.Cts.IsCancellationRequested);
        }

        public static async Task CheckGCStats()
        {
            do
            {
                using var process = Process.GetCurrentProcess();
                Config.Log.Info($"Process memory stats:\n" +
                                $"Private: {process.PrivateMemorySize64}\n" +
                                $"Working set: {process.WorkingSet64}\n" +
                                $"Virtual: {process.VirtualMemorySize64}\n" +
                                $"Paged: {process.PagedMemorySize64}\n" +
                                $"Paged sytem: {process.PagedSystemMemorySize64}\n" +
                                $"Non-pated system: {process.NonpagedSystemMemorySize64}");
                var processMemory = process.PrivateMemorySize64;
                var gcMemory = GC.GetTotalMemory(false);
                if (processMemory / (double)gcMemory > 2)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Optimized, true, true); // force LOH compaction
                }
                await Task.Delay(TimeSpan.FromHours(1)).ConfigureAwait(false);
            } while (!Config.Cts.IsCancellationRequested);
        }
    }
}
