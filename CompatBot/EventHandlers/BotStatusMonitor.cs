using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers;

internal static class BotStatusMonitor
{
    public static async Task RefreshAsync(DiscordClient client)
    {
        try
        {
            await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
            var status = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-activity").ConfigureAwait(false);
            var txt = await db.BotState.FirstOrDefaultAsync(s => s.Key == "bot-status-text").ConfigureAwait(false);
            var msg = txt?.Value;
            if (Enum.TryParse<DiscordActivityType>(status?.Value ?? "Watching", true, out var activity)
                && msg is {Length: >0})
                await client.UpdateStatusAsync(new(msg, activity), DiscordUserStatus.Online).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }
}