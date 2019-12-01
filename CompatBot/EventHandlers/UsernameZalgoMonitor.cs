using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    public static class UsernameZalgoMonitor
    {
        private static readonly HashSet<char> OversizedChars = new HashSet<char>
        {
            '꧁', '꧂', '⎝', '⎠', '⧹', '⧸', '⎛', '⎞',
        };

        public static async Task OnUserUpdated(UserUpdateEventArgs args)
        {
            try
            {
                var m = args.Client.GetMember(args.UserAfter);
                if (NeedsRename(m.DisplayName))
                    await args.Client.ReportAsync("🔣 Potential display name issue",
                        $"User {m.GetMentionWithNickname()} has changed their __username__ and is now shown as **{m.DisplayName.Sanitize()}**\nSuggestion to rename: **{StripZalgo(m.DisplayName).Sanitize()}**",
                        null,
                        ReportSeverity.Medium);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task OnMemberUpdated(GuildMemberUpdateEventArgs args)
        {
            try
            {
                var name = args.Member.DisplayName;
                if (NeedsRename(name))
                    await args.Client.ReportAsync("🔣 Potential display name issue",
                        $"Member {args.Member.GetMentionWithNickname()} has changed their __display name__ and is now shown as **{name.Sanitize()}**\nSuggestion to rename: **{StripZalgo(name).Sanitize()}**",
                        null,
                        ReportSeverity.Medium);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static async Task OnMemberAdded(GuildMemberAddEventArgs args)
        {
            try
            {
                var name = args.Member.DisplayName;
                if (NeedsRename(name))
                    await args.Client.ReportAsync("🔣 Potential display name issue",
                        $"New member joined the server: {args.Member.GetMentionWithNickname()} and is shown as **{name.Sanitize()}**\nSuggestion to rename: **{StripZalgo(name).Sanitize()}**",
                        null,
                        ReportSeverity.Medium);
            }
            catch (Exception e)
            {
                Config.Log.Error(e);
            }
        }

        public static bool NeedsRename(string displayName)
        {
            displayName = displayName?.Normalize().TrimEager();
            return displayName != StripZalgo(displayName, 3);
        }

        public static string StripZalgo(string displayName, int level = 2)
        {
            displayName = displayName?.Normalize().TrimEager();
            if (string.IsNullOrEmpty(displayName))
                return "Mr Invisible Wannabe";

            var builder = new StringBuilder();
            bool skipLowSurrogate = false;
            int consecutive = 0;
            foreach (var c in displayName)
            {
                switch (char.GetUnicodeCategory(c))
                {
                    case UnicodeCategory.ModifierSymbol:
                    case UnicodeCategory.NonSpacingMark:
                        if (++consecutive < level)
                            builder.Append(c);
                        break;

                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
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
                                builder.Append(c);
                            consecutive = 0;
                        }
                        break;
                }
            }
            var result = builder.ToString().TrimEager();
            if (string.IsNullOrEmpty(result))
                return "Mr Fancy Unicode Pants";

            return result;
        }
    }
}
