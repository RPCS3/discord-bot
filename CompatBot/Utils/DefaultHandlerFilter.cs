using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    internal static class DefaultHandlerFilter
    {
        internal static bool IsFluff(DiscordMessage message)
        {
            if (message.Channel.IsPrivate)
                return true;

            if (message.Author.IsBot)
                return true;

            if (string.IsNullOrEmpty(message.Content) || message.Content.StartsWith(Config.CommandPrefix))
                return true;

            return false;
        }
    }
}
