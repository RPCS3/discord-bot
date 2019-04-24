using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CompatBot.Commands;
using DSharpPlus;

namespace CompatBot
{
    internal static class Watchdog
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);
        public static readonly ConcurrentQueue<DateTime> DisconnectTimestamps = new ConcurrentQueue<DateTime>();

        public static async Task Watch(DiscordClient client)
        {
            do
            {
                await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                if (DisconnectTimestamps.IsEmpty)
                    continue;

                try
                {
                    var ch = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                    await client.SendMessageAsync(ch, "Potential socket deadlock detected, reconnecting...").ConfigureAwait(false);
                    await client.ReconnectAsync(true).ConfigureAwait(false);
                    await Task.Delay(CheckInterval, Config.Cts.Token).ConfigureAwait(false);
                    if (DisconnectTimestamps.IsEmpty)
                    {
                        await client.SendMessageAsync(ch, "Looks like we're back in business").ConfigureAwait(false);
                        continue;
                    }

                    await client.SendMessageAsync(ch, "Hard reconnect failed, restarting...").ConfigureAwait(false);
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
