using System;
using System.Threading.Tasks;
using CompatBot.Database;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers
{
    internal static class BotStatusMonitor
    {
        public static async Task RefreshAsync(DiscordClient client)
        {
            if (client == null)
                return;

            try
            {
                using (var db = new BotDb())
                {
                    var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
                    var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
                    var msg = txt?.Value;
                    if (Enum.TryParse(status?.Value ?? "Watching", true, out ActivityType activity)
                        && !string.IsNullOrEmpty(msg))
                        await client.UpdateStatusAsync(new DiscordActivity(msg, activity), UserStatus.Online).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }
    }
}
