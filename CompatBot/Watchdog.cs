﻿using System.Collections.Concurrent;
using System.Diagnostics;
using CompatBot.Commands;
using CompatBot.EventHandlers;
using Microsoft.ApplicationInsights;
using NLog;

namespace CompatBot;

internal static class Watchdog
{
    public static readonly ConcurrentQueue<DateTime> DisconnectTimestamps = new();
    public static readonly Stopwatch TimeSinceLastIncomingMessage = Stopwatch.StartNew();
    private static bool IsOk => DisconnectTimestamps.IsEmpty && TimeSinceLastIncomingMessage.Elapsed < Config.IncomingMessageCheckIntervalInMin;
    private static DiscordClient? discordClient;

    public static async Task Watch(DiscordClient client)
    {
        discordClient = client;
        do
        {
            await Task.Delay(Config.SocketDisconnectCheckIntervalInSec, Config.Cts.Token).ConfigureAwait(false);
            if (IsOk)
                continue;

            try
            {
                Config.TelemetryClient?.TrackEvent("socket-deadlock-potential");
                Config.Log.Warn("Potential socket deadlock detected, reconnecting…");
                await client.ReconnectAsync().ConfigureAwait(false);
                await Task.Delay(Config.SocketDisconnectCheckIntervalInSec, Config.Cts.Token).ConfigureAwait(false);
                if (IsOk)
                {
                    Config.Log.Info("Looks like we're back in business");
                    continue;
                }

                Config.TelemetryClient?.TrackEvent("socket-deadlock-for-sure");
                Config.Log.Error("Hard reconnect failed, restarting…");
                Bot.Restart(Program.InvalidChannelId, $@"Restarted to reset potential socket deadlock (last incoming message event: {TimeSinceLastIncomingMessage.Elapsed:h\:mm\:ss} ago)");
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
            if (message.Contains("Session resumed"))
                DisconnectTimestamps.Clear();
        }
        else if (level == nameof(LogLevel.Warn))
        {
            if (message.Contains("Dispatch:PRESENCES_REPLACE")
                && discordClient != null)
                BotStatusMonitor.RefreshAsync(discordClient).ConfigureAwait(false).GetAwaiter().GetResult();
            else if (message.Contains("Pre-emptive ratelimit triggered"))
                Config.TelemetryClient?.TrackEvent("preemptive-rate-limit");
        }
        else if (level == nameof(LogLevel.Error))
        {
            if (message.Contains("System.Threading.Tasks.TaskSchedulerException")
                || message.Contains("System.OutOfMemoryException"))
                Bot.RestartNoSaving();
        }
        else if (level == nameof(LogLevel.Fatal))
        {
            if (message.Contains("Connection closed (-1, '')")
                || message.Contains("Socket connection terminated")
                || message.Contains("heartbeats were skipped. Issuing reconnect."))
                DisconnectTimestamps.Enqueue(DateTime.UtcNow);
        }
    }

    public static Task OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args)
    {
        if (Config.TelemetryClient is TelemetryClient tc)
        {
            var userToBotDelay = (DateTime.UtcNow - args.Message.Timestamp.UtcDateTime).TotalMilliseconds;
            var latency = c.GetConnectionLatency(Config.BotGuildId);
            tc.TrackMetric("gw-latency", latency.TotalMilliseconds);
            tc.TrackMetric("user-to-bot-latency", userToBotDelay);
            tc.TrackMetric("time-since-last-incoming-message", TimeSinceLastIncomingMessage.ElapsedMilliseconds);
        }
        return Task.CompletedTask;
    }
        
    public static async Task SendMetrics(DiscordClient client)
    {
        do
        {
            await Task.Delay(Config.MetricsIntervalInSec).ConfigureAwait(false);
            var gcMemInfo = GC.GetGCMemoryInfo();
            using var process = Process.GetCurrentProcess();
            if (Config.TelemetryClient is not TelemetryClient tc)
                continue;
                
            var latency = client.GetConnectionLatency(Config.BotGuildId);
            tc.TrackMetric("gw-latency", latency.TotalMilliseconds);
            tc.TrackMetric("memory-gc-total", gcMemInfo.HeapSizeBytes);
            tc.TrackMetric("memory-gc-load", gcMemInfo.MemoryLoadBytes);
            tc.TrackMetric("memory-gc-committed", gcMemInfo.TotalCommittedBytes);
            tc.TrackMetric("memory-process-private", process.PrivateMemorySize64);
            tc.TrackMetric("memory-process-ws", process.WorkingSet64);
            tc.TrackMetric("github-limit-remaining", GithubClient.Client.RateLimitRemaining);
            tc.Flush();
                
            if (gcMemInfo.TotalCommittedBytes > 3_000_000_000)
                Bot.Restart(Program.InvalidChannelId, "GC Memory overcommitment");
        } while (!Config.Cts.IsCancellationRequested);
    }

    public static async Task CheckGCStats()
    {
        do
        {
            var gcMemInfo = GC.GetGCMemoryInfo();
            using var process = Process.GetCurrentProcess();
            Config.Log.Info($"Process memory stats:\n" +
                            $"GC Heap: {gcMemInfo.HeapSizeBytes}\n" +
                            $"Private: {process.PrivateMemorySize64}\n" +
                            $"Working set: {process.WorkingSet64}\n" +
                            $"Virtual: {process.VirtualMemorySize64}\n" +
                            $"Paged: {process.PagedMemorySize64}\n" +
                            $"Paged system: {process.PagedSystemMemorySize64}\n" +
                            $"Non-paged system: {process.NonpagedSystemMemorySize64}");
            await Task.Delay(TimeSpan.FromHours(1)).ConfigureAwait(false);
        } while (!Config.Cts.IsCancellationRequested);
    }
}