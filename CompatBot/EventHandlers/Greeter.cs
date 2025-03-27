using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers;

internal static class Greeter
{
    public static async Task OnMemberAdded(DiscordClient _, GuildMemberAddedEventArgs args)
    {
        await using var db = BotDb.OpenRead();
        if (await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == "motd").ConfigureAwait(false) is {Text.Length: >0} explanation)
        {
            var dm = await args.Member.CreateDmChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync(explanation.Text, explanation.Attachment, explanation.AttachmentFilename).ConfigureAwait(false);
            Config.Log.Info($"Sent motd to {args.Member.GetMentionWithNickname()}");
        }
    }
}