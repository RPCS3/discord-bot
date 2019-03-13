using System.Globalization;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    public static class UsernameZalgoMonitor
    {
        public static async Task OnUserUpdated(UserUpdateEventArgs args)
        {
            var m = args.Client.GetMember(args.UserAfter);
            if (NeedsRename(m.DisplayName))
                await args.Client.ReportAsync("Potential display name issue",
                    $"User {m.GetMentionWithNickname()} has changed their __username__ from " +
                    $"**{args.UserBefore.Username.Sanitize()}#{args.UserBefore.Discriminator}** to " +
                    $"**{args.UserAfter.Username.Sanitize()}#{args.UserAfter.Discriminator}**",
                    null,
                    ReportSeverity.Medium);
        }

        public static async Task OnMemberUpdated(GuildMemberUpdateEventArgs args)
        {
            if (NeedsRename(args.NicknameAfter))
                await args.Client.ReportAsync("Potential display name issue",
                    $"Member {args.Member.GetMentionWithNickname()} has changed their __display name__ from " +
                    $"**{(args.NicknameBefore ?? args.Member.Username).Sanitize()}** to " +
                    $"**{args.Member.DisplayName.Sanitize()}**",
                    null,
                    ReportSeverity.Medium);
        }

        public static async Task OnMemberAdded(GuildMemberAddEventArgs args)
        {
            if (NeedsRename(args.Member.DisplayName))
                await args.Client.ReportAsync("Potential display name issue",
                    $"New member joined the server: {args.Member.GetMentionWithNickname()}",
                    null,
                    ReportSeverity.Medium);
        }

        public static bool NeedsRename(string displayName)
        {
            displayName = displayName?.Normalize().TrimEager();
            if (string.IsNullOrEmpty(displayName))
                return true;

            var consecutiveCombiningCharacters = 0;
            foreach (var c in displayName)
            {
                switch (char.GetUnicodeCategory(c))
                {
                    //case UnicodeCategory.ModifierLetter:
                    case UnicodeCategory.ModifierSymbol:
                    case UnicodeCategory.NonSpacingMark:
                        if (++consecutiveCombiningCharacters > 2)
                            return true;
                        break;

                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                        break;

                    default:
                        consecutiveCombiningCharacters = 0;
                        break;
                }
            }
            return false;
        }
    }
}
