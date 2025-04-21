using CompatBot.Utils.Extensions;

namespace CompatBot.EventHandlers;

internal static class Starbucks
{
    private static readonly Dictionary<DiscordEmoji, string> TextMap = new()
    {
        [DiscordEmoji.FromUnicode("Ⓜ")] = "M",
        [DiscordEmoji.FromUnicode("🅰")] = "A",
        [DiscordEmoji.FromUnicode("🅱")] = "B",
        [DiscordEmoji.FromUnicode("🆎")] = "AB",
        [DiscordEmoji.FromUnicode("🅾")] = "O",

        [DiscordEmoji.FromUnicode("🇦")] = "A",
        [DiscordEmoji.FromUnicode("🇧")] = "B",
        [DiscordEmoji.FromUnicode("🇨")] = "C",
        [DiscordEmoji.FromUnicode("🇩")] = "D",
        [DiscordEmoji.FromUnicode("🇪")] = "E",
        [DiscordEmoji.FromUnicode("🇫")] = "F",
        [DiscordEmoji.FromUnicode("🇬")] = "G",
        [DiscordEmoji.FromUnicode("🇭")] = "H",
        [DiscordEmoji.FromUnicode("🇮")] = "I",
        [DiscordEmoji.FromUnicode("🇯")] = "G",
        [DiscordEmoji.FromUnicode("🇰")] = "K",
        [DiscordEmoji.FromUnicode("🇱")] = "L",
        [DiscordEmoji.FromUnicode("🇲")] = "M",
        [DiscordEmoji.FromUnicode("🇳")] = "N",
        [DiscordEmoji.FromUnicode("🇴")] = "O",
        [DiscordEmoji.FromUnicode("🇵")] = "P",
        [DiscordEmoji.FromUnicode("🇶")] = "Q",
        [DiscordEmoji.FromUnicode("🇷")] = "R",
        [DiscordEmoji.FromUnicode("🇸")] = "S",
        [DiscordEmoji.FromUnicode("🇹")] = "T",
        [DiscordEmoji.FromUnicode("🇺")] = "U",
        [DiscordEmoji.FromUnicode("🇻")] = "V",
        [DiscordEmoji.FromUnicode("🇼")] = "W",
        [DiscordEmoji.FromUnicode("🇽")] = "X",
        [DiscordEmoji.FromUnicode("🇾")] = "Y",
        [DiscordEmoji.FromUnicode("🇿")] = "Z",

        [DiscordEmoji.FromUnicode("0\u20E3")] = "0",
        [DiscordEmoji.FromUnicode("1\u20E3")] = "1",
        [DiscordEmoji.FromUnicode("2\u20E3")] = "2",
        [DiscordEmoji.FromUnicode("3\u20E3")] = "3",
        [DiscordEmoji.FromUnicode("4\u20E3")] = "4",
        [DiscordEmoji.FromUnicode("5\u20E3")] = "5",
        [DiscordEmoji.FromUnicode("6\u20E3")] = "6",
        [DiscordEmoji.FromUnicode("7\u20E3")] = "7",
        [DiscordEmoji.FromUnicode("8\u20E3")] = "8",
        [DiscordEmoji.FromUnicode("9\u20E3")] = "9",
        [DiscordEmoji.FromUnicode("🔟")] = "10",
        [DiscordEmoji.FromUnicode("💯")] = "100",

        [DiscordEmoji.FromUnicode("🆑")] = "CL",
        [DiscordEmoji.FromUnicode("🆒")] = "COOL",
        [DiscordEmoji.FromUnicode("🆓")] = "FREE",
        [DiscordEmoji.FromUnicode("🆔")] = "ID",
        [DiscordEmoji.FromUnicode("🆕")] = "NEW",
        [DiscordEmoji.FromUnicode("🆖")] = "NG",
        [DiscordEmoji.FromUnicode("🆗")] = "OK",
        [DiscordEmoji.FromUnicode("🆘")] = "SOS",
        [DiscordEmoji.FromUnicode("🆙")] = "UP",
        [DiscordEmoji.FromUnicode("🆚")] = "VS",
        [DiscordEmoji.FromUnicode("⭕")] = "O",
        [DiscordEmoji.FromUnicode("🔄")] = "O",
        [DiscordEmoji.FromUnicode("✝")] = "T",
        [DiscordEmoji.FromUnicode("❌")] = "X",
        [DiscordEmoji.FromUnicode("✖")] = "X",
        [DiscordEmoji.FromUnicode("❎")] = "X",
        [DiscordEmoji.FromUnicode("🅿")] = "P",
        [DiscordEmoji.FromUnicode("🚾")] = "WC",
        [DiscordEmoji.FromUnicode("ℹ️")] = "i",
        [DiscordEmoji.FromUnicode("〰")] = "W",
    };

    public static Task Handler(DiscordClient c, MessageReactionAddedEventArgs args)
        => CheckMessageAsync(c, args.Channel, args.User, args.Message, args.Emoji, false);

    public static async Task CheckBacklogAsync(DiscordClient client, DiscordGuild guild)
    {
        try
        {
            var after = DateTime.UtcNow - Config.ModerationBacklogThresholdInHours;
            var checkTasks = new List<Task>();
            foreach (var channel in guild.Channels.Values.Where(ch => Config.Moderation.MediaChannels.Contains(ch.Id)))
            {
                var messages = await channel.GetMessagesCachedAsync().ConfigureAwait(false);
                var messagesToCheck = from msg in messages
                    where msg.CreationTimestamp > after && msg.Reactions.Any(r => r.Emoji == Config.Reactions.Starbucks && r.Count >= Config.Moderation.StarbucksThreshold)
                    select msg;
                foreach (var message in messagesToCheck)
                {
                    var reactionUsers = message.GetReactionsAsync(Config.Reactions.Starbucks).ToList();
                    if (reactionUsers.Count > 0)
                        checkTasks.Add(CheckMessageAsync(client, channel, reactionUsers[0], message, Config.Reactions.Starbucks, true));
                }
            }
            await Task.WhenAll(checkTasks).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    private static async Task CheckMessageAsync(DiscordClient client, DiscordChannel? channel, DiscordUser user, DiscordMessage message, DiscordEmoji emoji, bool isBacklog)
    {
        try
        {
            if (user.IsBotSafeCheck() || channel is null || channel.IsPrivate)
                return;

            // in case it's not in cache and doesn't contain any info, including Author
            message = await channel.GetMessageAsync(message.Id).ConfigureAwait(false);
            if (emoji == Config.Reactions.Starbucks)
                await CheckMediaTalkAsync(client, channel, message, emoji).ConfigureAwait(false);
            if (emoji == Config.Reactions.ShutUp && !isBacklog)
                await ShutupAsync(client, user, message).ConfigureAwait(false);
            if (emoji == Config.Reactions.BadUpdate && !isBacklog)
                await BadUpdateAsync(client, user, message, emoji).ConfigureAwait(false);
            await CheckGameFansAsync(client, channel, message).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    private static async ValueTask CheckMediaTalkAsync(DiscordClient client, DiscordChannel channel, DiscordMessage message, DiscordEmoji emoji)
    {
        if (!Config.Moderation.MediaChannels.Contains(channel.Id))
            return;

        // message.Timestamp throws if it's not in the cache AND is in local time zone
        if (DateTime.UtcNow - message.CreationTimestamp > Config.ModerationBacklogThresholdInHours)
            return;

        if (message.Reactions.Any(r => r.Emoji == emoji && (r.IsMe || r.Count < Config.Moderation.StarbucksThreshold)))
            return;

        if (await message.Author.IsWhitelistedAsync(client, channel.Guild).ConfigureAwait(false))
            return;

        var users = message.GetReactionsAsync(emoji).ToList();
        if (users.Any(u => u.IsCurrent))
            return;

        var members = users
            .Distinct()
            .Select(u => channel.Guild
                .GetMemberAsync(u.Id)
                .ContinueWith(ct => ct.IsCompletedSuccessfully ? ct : Task.FromResult((DiscordMember?)null), TaskScheduler.Default))
            .ToList() //force eager task creation
            .Select(t => t.Unwrap().ConfigureAwait(false).GetAwaiter().GetResult())
            .Where(m => m != null)
            .ToList();
        var reporters = members.Where(m => m!.Roles.Any()).ToList();
        if (reporters.Count < Config.Moderation.StarbucksThreshold)
            return;

        await message.ReactWithAsync(emoji).ConfigureAwait(false);
        await client.ReportAsync(Config.Reactions.Starbucks + " Media talk report", message, reporters, null, ReportSeverity.Medium).ConfigureAwait(false);
    }

    private static async ValueTask ShutupAsync(DiscordClient client, DiscordUser user, DiscordMessage message)
    {
        if (message.Author is null || message.Author.IsCurrent is false)
            return;

        if (message.CreationTimestamp.Add(Config.ShutupTimeLimitInMin) < DateTime.UtcNow)
            return;

        if (!await user.IsWhitelistedAsync(client, message.Channel?.Guild).ConfigureAwait(false))
            return;

        await message.DeleteAsync().ConfigureAwait(false);
    }

    private static async ValueTask BadUpdateAsync(DiscordClient client, DiscordUser user, DiscordMessage message, DiscordEmoji emoji)
    {
        if (message.Channel?.Id != Config.BotChannelId)
            return;

        if (!await user.IsSmartlistedAsync(client, message.Channel.Guild).ConfigureAwait(false))
            return;

        await ToggleBadUpdateAnnouncementAsync(message).ConfigureAwait(false);
        try
        {
            await message.DeleteReactionAsync(emoji, user).ConfigureAwait(false);
        }
        catch { }
    }

    internal static async ValueTask ToggleBadUpdateAnnouncementAsync(DiscordMessage message)
    {
        if (message.Embeds is not [DiscordEmbed embed])
            return;
        
        var result = new DiscordEmbedBuilder(embed);
        const string warningTitle = "Warning!";
        if (embed.Color?.Value == Config.Colors.UpdateStatusGood.Value)
        {
            result = result.WithColor(Config.Colors.UpdateStatusBad);
            result.ClearFields();
            var warned = false;
            foreach (var f in embed.Fields!)
            {
                if (!warned)
                {
                    result.AddField(warningTitle, "This build is known to have severe problems, please avoid downloading.");
                    warned = true;
                }
                result.AddField(f.Name!, f.Value!, f.Inline);
            }
        }
        else if (embed.Color?.Value == Config.Colors.UpdateStatusBad.Value)
        {
            result = result.WithColor(Config.Colors.UpdateStatusGood);
            result.ClearFields();
            foreach (var f in embed.Fields!)
            {
                if (f.Name is warningTitle)
                    continue;

                result.AddField(f.Name!, f.Value!, f.Inline);
            }
        }
        await message.UpdateOrCreateMessageAsync(message.Channel!, embed: result).ConfigureAwait(false);
    }

    private static async ValueTask CheckGameFansAsync(DiscordClient client, DiscordChannel channel, DiscordMessage message)
    {
        var bot = await client.GetMemberAsync(channel.Guild, client.CurrentUser).ConfigureAwait(false);
        var ch = channel.IsPrivate ? channel.Users.FirstOrDefault(u => u.Id != client.CurrentUser.Id)?.Username + "'s DM" : "#" + channel.Name;
        if (!channel.PermissionsFor(bot).HasPermission(DiscordPermission.AddReactions))
        {
            Config.Log.Debug($"No permissions to react in {ch}");
            return;
        }

        var mood = client.GetEmoji(":sqvat:", "😒");
        if (message.Reactions.Any(r => r.Emoji == mood && r.IsMe))
            return;

        var reactionMsg = string.Concat(message.Reactions.Select(r => TextMap.TryGetValue(r.Emoji, out var txt) ? txt : " ")).Trim();
        if (string.IsNullOrEmpty(reactionMsg))
            return;

        Config.Log.Debug($"Emoji text: {reactionMsg} (in {ch})");

        if (reactionMsg.Contains("UFC"))
        {
            await message.CreateReactionAsync(mood).ConfigureAwait(false);
            await message.CreateReactionAsync(DiscordEmoji.FromUnicode("🇳")).ConfigureAwait(false);
            await message.CreateReactionAsync(DiscordEmoji.FromUnicode("🇴")).ConfigureAwait(false);
        }
    }
}