using System.Text.RegularExpressions;

namespace CompatBot.EventHandlers;

public static partial class UsernameRaidMonitor
{
    [GeneratedRegex(@"\b(Discord|Wang\s*Chuanfu)\b", RegexOptions.ExplicitCapture)]
    private static partial Regex SpamNickname();

    public static async Task OnMemberUpdated(DiscordClient c, GuildMemberUpdatedEventArgs args)
    {
        try
        {
            //member object most likely will not be updated in client cache at this moment
            var member = await args.Guild.GetMemberAsync(args.Member.Id).ConfigureAwait(false) ?? args.Member;
            string? fallback;
            if (args.NicknameAfter is string name)
                fallback = member.Username;
            else
            {
                name = member.Username;
                fallback = null;
            }
            if (NeedsKick(name))
            {
                //await member.RemoveAsync("Anti Raid").ConfigureAwait(false);
                await c.ReportAsync("🤖 Potential scam bot",
                    $"""
                    User {member.GetMentionWithNickname()} who changed their nickname to {name} as a potential scam bot
                    """,
                    null,
                    ReportSeverity.Medium
                );
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
            var member = await args.Guild.GetMemberAsync(args.Member.Id).ConfigureAwait(false) ?? args.Member;
            var name = member.DisplayName;
            if (NeedsKick(name))
            {
                await member.RemoveAsync("Anti Raid").ConfigureAwait(false);
                await c.ReportAsync("🤖 Potential scam bot",
                    $"""
                    Kicked user {member.GetMentionWithNickname()} who has joined with nickname {name}, and is a potential scam bot
                    """,
                    null,
                    ReportSeverity.Low
                );
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e);
        }
    }

    public static bool NeedsKick(string displayName)
    {
        displayName = displayName.Normalize().TrimEager();
        return SpamNickname().IsMatch(displayName);
    }
}