using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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
                    await client.SendMessageAsync(ch, "Potential socket deadlock detected, restarting...").ConfigureAwait(false);
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
