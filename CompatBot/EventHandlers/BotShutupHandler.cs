using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using NReco.Text;

namespace CompatBot.EventHandlers
{
    internal static class BotShutupHandler
    {
        private static readonly AhoCorasickDoubleArrayTrie<string> ChillCheck = new AhoCorasickDoubleArrayTrie<string>(new[] { "shut the fuck up", "shut up", "shutup", "hush", "chill" }.ToDictionary(s => s, s => s), true);

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (args.Channel.IsPrivate)
                return;

            if (args.Author.IsBot)
                return;

            if (string.IsNullOrEmpty(args.Message.Content) || args.Message.Content.StartsWith(Config.CommandPrefix))
                return;

            if (!args.Author.IsWhitelisted(args.Client, args.Guild))
                return;

            if (!NeedToSilence(args.Message))
                return;

            await args.Message.ReactWithAsync(args.Client, DiscordEmoji.FromUnicode("😟"), "Okay (._.)").ConfigureAwait(false);
            var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddSeconds(-30)).ConfigureAwait(false);
            if (lastBotMessages.OrderByDescending(m => m.CreationTimestamp).FirstOrDefault(m => m.Author.IsCurrent) is DiscordMessage msg)
                await msg.DeleteAsync("asked to shut up").ConfigureAwait(false);
        }

        internal static bool NeedToSilence(DiscordMessage msg)
        {
            if (string.IsNullOrEmpty(msg.Content))
                return false;

            var needToChill = false;
            ChillCheck.ParseText(msg.Content, h =>
            {
                needToChill = true;
                return false;
            });
            if (!needToChill)
                return false;

            return msg.Content.Contains("bot") || (msg.MentionedUsers?.Any(u => u.IsCurrent) ?? false);
        }

    }
}
