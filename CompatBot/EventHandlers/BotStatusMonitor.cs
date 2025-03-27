using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers;

internal static class BotStatusMonitor
{
    public static async Task RefreshAsync(DiscordClient client)
    {
        try
        {
            await using var db = BotDb.OpenRead();
            var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
            var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
            var msg = txt?.Value;
            if (Enum.TryParse(status?.Value ?? "Watching", true, out DiscordActivityType activity)
                && !string.IsNullOrEmpty(msg))
                await client.UpdateStatusAsync(new DiscordActivity(msg, activity), DiscordUserStatus.Online).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }
}