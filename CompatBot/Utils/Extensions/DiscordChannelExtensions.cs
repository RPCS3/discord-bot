namespace CompatBot.Utils;

internal static class DiscordChannelExtensions
{
    internal static bool IsHelpChannel(this DiscordChannel channel)
        => channel.IsPrivate
           || channel.Name.Contains("help", StringComparison.OrdinalIgnoreCase)
           || channel.Name.Equals("donors", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOfftopicChannel(this DiscordChannel channel)
        => channel.Name.Contains("off-topic", StringComparison.InvariantCultureIgnoreCase)
           || channel.Name.Contains("offtopic", StringComparison.InvariantCultureIgnoreCase);

    internal static bool IsSpamChannel(this DiscordChannel channel)
        => channel.IsPrivate
           || channel.Name.Contains("spam", StringComparison.OrdinalIgnoreCase)
           || channel.Name.Equals("testers", StringComparison.OrdinalIgnoreCase);
}