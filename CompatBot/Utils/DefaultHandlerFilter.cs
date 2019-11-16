using CompatBot.Utils.Extensions;
using DSharpPlus.Entities;

namespace CompatBot.Utils
{
    internal static class DefaultHandlerFilter
    {
        internal static bool IsFluff(DiscordMessage message)
        {
            if (message == null)
                return true;

            if (message.Author.IsBotSafeCheck())
                return true;

            if (string.IsNullOrEmpty(message.Content)
                || message.Content.StartsWith(Config.CommandPrefix)
                || message.Content.StartsWith(Config.AutoRemoveCommandPrefix))
                return true;

            return false;
        }
    }
}
