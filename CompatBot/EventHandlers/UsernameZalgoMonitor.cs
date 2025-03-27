using System.Globalization;
using CompatApiClient.Utils;
using CompatBot.Database;

namespace CompatBot.EventHandlers;

public static class UsernameZalgoMonitor
{
    private static readonly HashSet<char> OversizedChars =
    [
        '꧁', '꧂', '⎝', '⎠', '⧹', '⧸', '⎛', '⎞', '﷽', '⸻', 'ဪ', '꧅', '꧄', '˞',
    ];

    public static async Task OnUserUpdated(DiscordClient c, UserUpdatedEventArgs args)
    {
        try
        {
            if (await c.GetMemberAsync(args.UserAfter).ConfigureAwait(false) is DiscordMember m
                && await NeedsRenameAsync(m.DisplayName).ConfigureAwait(false))
            {
                var suggestedName = await StripZalgoAsync(m.DisplayName, m.Username, m.Id).ConfigureAwait(false);
                suggestedName = suggestedName.Sanitize();
                await c.ReportAsync("🔣 Potential display name issue",
                    $"""
                        User {m.GetMentionWithNickname()} has changed their __username__ and is now shown as **{m.DisplayName.Sanitize()}**
                        Automatically renamed to: **{suggestedName}**
                        """,
                    null,
                    ReportSeverity.Low);
                await DmAndRenameUserAsync(c, m, suggestedName).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public static async Task OnMemberUpdated(DiscordClient c, GuildMemberUpdatedEventArgs args)
    {
        try
        {
            //member object most likely will not be updated in client cache at this moment
            string? fallback;
            if (args.NicknameAfter is string name)
                fallback = args.Member.Username;
            else
            {
                name = args.Member.Username;
                fallback = null;
            }

            var member = await args.Guild.GetMemberAsync(args.Member.Id).ConfigureAwait(false) ?? args.Member;
            if (await NeedsRenameAsync(name).ConfigureAwait(false))
            {
                var suggestedName = await StripZalgoAsync(name, fallback, args.Member.Id).ConfigureAwait(false);
                suggestedName = suggestedName.Sanitize();
                await c.ReportAsync("🔣 Potential display name issue",
                    $"""
                        Member {member.GetMentionWithNickname()} has changed their __display name__ and is now shown as **{name.Sanitize()}**
                        Automatically renamed to: **{suggestedName}**
                        """,
                    null,
                    ReportSeverity.Low);
                await DmAndRenameUserAsync(c, member, suggestedName).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public static async Task OnMemberAdded(DiscordClient c, GuildMemberAddedEventArgs args)
    {
        try
        {
            var name = args.Member.DisplayName;
            if (await NeedsRenameAsync(name).ConfigureAwait(false))
            {
                var suggestedName = await StripZalgoAsync(name, args.Member.Username, args.Member.Id).ConfigureAwait(false);
                suggestedName = suggestedName.Sanitize();
                await c.ReportAsync("🔣 Potential display name issue",
                    $"""
                        New member joined the server: {args.Member.GetMentionWithNickname()} and is shown as **{name.Sanitize()}**
                        Automatically renamed to: **{suggestedName}**
                        """,
                    null,
                    ReportSeverity.Low);
                await DmAndRenameUserAsync(c, args.Member, suggestedName).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public static async ValueTask<bool> NeedsRenameAsync(string displayName)
    {
        displayName = displayName.Normalize().TrimEager();
        return displayName != await StripZalgoAsync(displayName, null, 0ul, NormalizationForm.FormC, 3).ConfigureAwait(false);
    }

    private static async Task DmAndRenameUserAsync(DiscordClient client, DiscordMember member, string suggestedName)
    {
        try
        {
            var renameTask = member.ModifyAsync(m => m.Nickname = suggestedName);
            Config.Log.Info($"Renamed {member.Username}#{member.Discriminator} ({member.Id}) to {suggestedName}");
            var rulesChannel = await client.GetChannelAsync(Config.BotRulesChannelId).ConfigureAwait(false);
            var msg = $"""
                Hello, your current _display name_ is breaking {rulesChannel.Mention} #7, so you have been renamed to `{suggestedName}`.
                I'm not perfect and can't clean all the junk in names in some cases, so change your nickname at your discretion.
                You can change your _display name_ by clicking on the server name at the top left and selecting **Change Nickname**.
                """;
            var dm = await member.CreateDmChannelAsync().ConfigureAwait(false);
            await dm.SendMessageAsync(msg).ConfigureAwait(false);
            await renameTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e);
        }
    }

    public static async ValueTask<string> StripZalgoAsync(string displayName, string? userName, ulong userId, NormalizationForm normalizationForm = NormalizationForm.FormD, int level = 0)
    {
        const int minNicknameLength = 2;
        displayName = displayName.Normalize(normalizationForm).TrimEager();
        if (displayName is null or {Length: <minNicknameLength} && userName is not null)
            displayName = userName.Normalize(normalizationForm).TrimEager();
        if (displayName is null or {Length: <minNicknameLength})
            return await GenerateRandomNameAsync(userId).ConfigureAwait(false);

        var builder = new StringBuilder();
        bool skipLowSurrogate = false;
        int consecutive = 0;
        int codePoint = 0;
        char highSurrogate = '\0';
        bool hasNormalCharacterBefore = false;
        foreach (var c in displayName)
        {
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.NonSpacingMark:
                    if (++consecutive < level && hasNormalCharacterBefore)
                        builder.Append(c);
                    break;

                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.PrivateUse:
                    break;

                case UnicodeCategory.Surrogate:
                    if (char.IsHighSurrogate(c))
                    {
                        codePoint = 0x10000 | ((c & 0x3ff) << 10);
                        highSurrogate = c;
                    }
                    else
                    {
                        codePoint |= c & 0x3ff;
                        if (codePoint is >= 0x016a0 and < 0x01700  // Runic
                            or >= 0x101d0 and < 0x10200  // Phaistos Disc
                            or >= 0x10380 and < 0x10400  // Ugaritic and Old Persian
                            or >= 0x12000 and < 0x13000) // Cuneiform
                            continue;

                        builder.Append(highSurrogate).Append(c);
                        hasNormalCharacterBefore = true;
                        consecutive = 0;
                    }
                    break;
                    
                case UnicodeCategory.OtherNotAssigned when c >= 0xdb40:
                    skipLowSurrogate = true;
                    break;

                default:
                    if (char.IsLowSurrogate(c) && skipLowSurrogate)
                        skipLowSurrogate = false;
                    else
                    {
                        if (!OversizedChars.Contains(c))
                        {
                            builder.Append(c);
                            hasNormalCharacterBefore = true;
                            consecutive = 0;
                        }
                    }
                    break;
            }
        }
        var result = builder.ToString().TrimEager();
        if (result is null or {Length: <minNicknameLength})
        {
            if (userName is null)
                return await GenerateRandomNameAsync(userId).ConfigureAwait(false);
            return await StripZalgoAsync(userName, null, userId, normalizationForm, level).ConfigureAwait(false);
        }
        return result;
    }

    public static async ValueTask<string> GenerateRandomNameAsync(ulong userId)
    {
        var hash = userId.GetHashCode();
        var rng = new Random(hash);
        await using var db = await ThumbnailDb.OpenReadAsync().ConfigureAwait(false);
        var count = db.NamePool.Count();
        var name = db.NamePool.Skip(rng.Next(count)).First().Name;
        return name + Config.RenameNameSuffix;
    }
}