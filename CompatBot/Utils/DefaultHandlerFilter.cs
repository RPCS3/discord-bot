using CompatBot.Utils.Extensions;

namespace CompatBot.Utils;

internal static class DefaultHandlerFilter
{
    internal static bool IsFluff(DiscordMessage? message)
    {
        if (message is null)
            return true;

        if (message.Author.IsBotSafeCheck())
            return true;

        if (string.IsNullOrEmpty(message.Content)
            || message.Content.StartsWith(Config.CommandPrefix)
            || message.Content.StartsWith(Config.AutoRemoveCommandPrefix))
            return true;

        return false;
    }

    internal static bool IsOnionLike(this CommandContext ctx)
        => IsOnionLike(ctx.Message);
        
    internal static bool IsOnionLike(this DiscordMessage message)
        => !message.Channel.IsPrivate
           && (message.Author.Id == 197163728867688448ul
               || message.Author.Id == 272022580112654336ul && new Random().NextDouble() < 0.1 * Config.FunMultiplier);
}