using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;
using CompatBot.Commands;
using DSharpPlus;

namespace CompatBot
{
    internal static class Watchdog
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan IncomingMessageCheckInterval = TimeSpan.FromMinutes(10);
        public static readonly ConcurrentQueue<DateTime> DisconnectTimestamps = new ConcurrentQueue<DateTime>();
        public static readonly Stopwatch TimeSinceLastIncomingMessage = Stopwatch.StartNew();

        public static async Task Watch(DiscordClient client)
        {
            do
            {
                await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                if (DisconnectTimestamps.IsEmpty && TimeSinceLastIncomingMessage.Elapsed < IncomingMessageCheckInterval)
                    continue;

                try
                {
                    Config.Log.Warn("Potential socket deadlock detected, reconnecting...");
                    await client.ReconnectAsync(true).ConfigureAwait(false);
                    await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                    if (DisconnectTimestamps.IsEmpty)
                    {
                        Config.Log.Info("Looks like we're back in business");
                        continue;
                    }
                    Config.Log.Error("Hard reconnect failed, restarting...");
                    Sudo.Bot.Restart(Program.InvalidChannelId);
                }
                catch (Exception e)
                {
                    Config.Log.Error(e);
                }
            } while (!Config.Cts.IsCancellationRequested);
        }
    }
}
