using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal class LimitedToSpamChannel: CheckBaseAttribute
    {
        public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help)
        {
            if (help || IsSpamChannel(ctx.Channel))
                return true;

            try
            {
                var msgList = await ctx.Channel.GetMessagesCachedAsync(10).ConfigureAwait(false);
                if (msgList.Any(m => m.Author.IsCurrent
                                     && m.Content is string s
                                     && s.Contains(ctx.Command.QualifiedName, StringComparison.InvariantCultureIgnoreCase)))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    return false; // we just explained to use #bot-spam or DMs, can't help if people can't read
                }
            }
            catch {}

            var spamChannel = await ctx.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
            await ctx.RespondAsync($"`{ctx.Prefix}{ctx.Command.QualifiedName}` is limited to {spamChannel.Mention} and DMs").ConfigureAwait(false);
            return false;
        }

        internal static bool IsSpamChannel(DiscordChannel channel)
        {
            return channel.IsPrivate || channel.Name.Contains("spam", StringComparison.InvariantCultureIgnoreCase);
        }
    }
}