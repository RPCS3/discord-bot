﻿using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.EventHandlers;

internal static class Greeter
{
    public static async Task OnMemberAdded(DiscordClient _, GuildMemberAddEventArgs args)
    {
        await using var db = new BotDb();
        var explanation = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == "motd").ConfigureAwait(false);
        if (explanation != null)
        {
            var dm = await args.Member.CreateDmChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync(explanation.Text, explanation.Attachment, explanation.AttachmentFilename).ConfigureAwait(false);
            Config.Log.Info($"Sent motd to {args.Member.GetMentionWithNickname()}");
        }
    }
}