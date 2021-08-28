﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    public static class UsernameZalgoMonitor
    {
        private static readonly HashSet<char> OversizedChars = new()
        {
            '꧁', '꧂', '⎝', '⎠', '⧹', '⧸', '⎛', '⎞', '﷽', '⸻', 'ဪ', '꧅', '꧄', '˞',
        };

        public static async Task OnUserUpdated(DiscordClient c, UserUpdateEventArgs args)
        {
            try
            {
                if (c.GetMember(args.UserAfter) is DiscordMember m
                    && NeedsRename(m.DisplayName))
                {
                    var suggestedName = StripZalgo(m.DisplayName, m.Username, m.Id).Sanitize();
                    await c.ReportAsync("🔣 Potential display name issue",
                        $"User {m.GetMentionWithNickname()} has changed their __username__ and is now shown as **{m.DisplayName.Sanitize()}**\nAutomatically renamed to: **{suggestedName}**",
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

        public static async Task OnMemberUpdated(DiscordClient c, GuildMemberUpdateEventArgs args)
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
                if (NeedsRename(name))
                {
                    var suggestedName = StripZalgo(name, fallback, args.Member.Id).Sanitize();
                    await c.ReportAsync("🔣 Potential display name issue",
                        $"Member {member.GetMentionWithNickname()} has changed their __display name__ and is now shown as **{name.Sanitize()}**\nAutomatically renamed to: **{suggestedName}**",
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

        public static async Task OnMemberAdded(DiscordClient c, GuildMemberAddEventArgs args)
        {
            try
            {
                var name = args.Member.DisplayName;
                if (NeedsRename(name))
                {
                    var suggestedName = StripZalgo(name, args.Member.Username, args.Member.Id).Sanitize();
                    await c.ReportAsync("🔣 Potential display name issue",
                        $"New member joined the server: {args.Member.GetMentionWithNickname()} and is shown as **{name.Sanitize()}**\nAutomatically renamed to: **{suggestedName}**",
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

        public static bool NeedsRename(string displayName)
        {
            displayName = displayName.Normalize().TrimEager();
            return displayName != StripZalgo(displayName, null, 0ul, NormalizationForm.FormC, 3);
        }

        private static async Task DmAndRenameUserAsync(DiscordClient client, DiscordMember member, string suggestedName)
        {
            try
            {
                var renameTask = member.ModifyAsync(m => m.Nickname = suggestedName);
                Config.Log.Info($"Renamed {member.Username}#{member.Discriminator} ({member.Id}) to {suggestedName}");
                var rulesChannel = await client.GetChannelAsync(Config.BotRulesChannelId).ConfigureAwait(false);
                var msg = $"Hello, your current _display name_ is breaking {rulesChannel.Mention} #7, so you have been renamed to `{suggestedName}`.\n" +
                          "I'm not perfect and can't clean all the junk in names in some cases, so change your nickname at your discretion.\n" +
                          "You can change your _display name_ by clicking on the server name at the top left and selecting **Change Nickname**.";
                var dm = await member.CreateDmChannelAsync().ConfigureAwait(false);
                await dm.SendMessageAsync(msg).ConfigureAwait(false);
                await renameTask.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        public static string StripZalgo(string displayName, string? userName, ulong userId, NormalizationForm normalizationForm = NormalizationForm.FormD, int level = 0)
        {
            const int minNicknameLength = 2;
            displayName = displayName.Normalize(normalizationForm).TrimEager();
            if (displayName is null or {Length: <minNicknameLength} && userName is not null)
                displayName = userName.Normalize(normalizationForm).TrimEager();
            if (displayName is null or {Length: <minNicknameLength})
                return GenerateRandomName(userId);

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
                    case UnicodeCategory.ModifierSymbol:
                    case UnicodeCategory.NonSpacingMark:
                        if (++consecutive < level && hasNormalCharacterBefore)
                            builder.Append(c);
                        break;

                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
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
                    return GenerateRandomName(userId);
                return StripZalgo(userName, null, userId, normalizationForm, level);
            }
            return result;
        }

        public static string GenerateRandomName(ulong userId)
        {
            var hash = userId.GetHashCode();
            var rng = new Random(hash);
            using var db = new ThumbnailDb();
            var count = db.NamePool.Count();
            var name = db.NamePool.Skip(rng.Next(count)).First().Name;
            return name + Config.RenameNameSuffix;
        }
    }
}
