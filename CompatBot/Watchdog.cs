using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Database.Providers;
using DSharpPlus;
using Microsoft.ApplicationInsights;

namespace CompatBot
{
    internal static class Watchdog
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
        public static readonly ConcurrentQueue<DateTime> DisconnectTimestamps = new ConcurrentQueue<DateTime>();
        public static readonly Stopwatch TimeSinceLastIncomingMessage = Stopwatch.StartNew();
        private static bool IsOk => DisconnectTimestamps.IsEmpty && TimeSinceLastIncomingMessage.Elapsed < Config.IncomingMessageCheckIntervalInMinutes;

        public static async Task Watch(DiscordClient client)
        {
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

        public static async Task SendMetrics(DiscordClient client)
        {
            do
            {
                await Task.Delay(Config.MetricsIntervalInSeconds).ConfigureAwait(false);
                if (Config.TelemetryClient is TelemetryClient tc)
                {
                    tc.TrackMetric("gw-latency", client.Ping);
                    tc.TrackMetric("time-since-last-incoming-message", TimeSinceLastIncomingMessage.ElapsedMilliseconds);
                    tc.TrackMetric("gc-total-memory", GC.GetTotalMemory(false));
                    tc.TrackMetric("github-limit-remaining", GithubClient.Client.RateLimitRemaining);
                }
            } while (!Config.Cts.IsCancellationRequested);
        }
    }
}
