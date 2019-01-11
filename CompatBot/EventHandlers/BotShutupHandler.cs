using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using NReco.Text;

namespace CompatBot.EventHandlers
{
    internal static class BotShutupHandler
    {
        private static readonly AhoCorasickDoubleArrayTrie<bool> ChillCheck = new AhoCorasickDoubleArrayTrie<bool>(new[]
        {
            "shut the fuck up", "shut up", "shutup", "shuddup", "hush", "chill", "bad bot",
            "no one asked you", "useless bot",
            "take this back", "take that back",
            "delete this", "delete that",
            "remove this", "remove that",
        }.ToDictionary(s => s, _ => true).Concat(
            new[]
            {
                "good bot", "gud bot", "good boy", "goodboy", "gud boy", "gud boi",
                "thank you", "thankyou", "thnk", "thnx", "thnku", "thank u", "tnx",
                "arigato", "aregato", "arigatou", "aregatou", "oregato", "origato",
                "poor bot",
            }.ToDictionary(s => s, _ => false)
        ), true);

        private static readonly DiscordEmoji[] SadReactions = new[]
        {
            "😶", "😣", "😥", "🤐", "😯", "😫", "😓", "😔", "😕", "☹",
            "🙁", "😖", "😞", "😟", "😢", "😭", "😦", "😧", "😨", "😩",
            "😰", "🥺", "🙊",
        }.Select(DiscordEmoji.FromUnicode).ToArray();

        private static readonly string[] SadMessages =
        {
            "Okay (._.)", "As you wish", "My bad", "I only wanted to help", "Dobby will learn, master",
            "Sorry...", "I'll try to be smarter next time", "Your wish is my command", "Done.",
        };

        private static readonly DiscordEmoji[] ThankYouReactions = new[]
        {
            "😊", "😘", "😍", "🤗", "😳",
            "🙌", "✌", "👌", "👋", "🤟", "🙏", "🤝",
            "🎉", "✨",
            "❤", "🧡", "💛", "💙", "💚", "💜", "💖",
        }.Select(DiscordEmoji.FromUnicode).ToArray();

        private static readonly string[] ThankYouMessages =
        {
            "Aww", "I'm here to help", "Always a pleasure", "Thank you", "Good word is always appreciated",
            "Glad I could help", "I try my best", "Blessed day", "It is officially a good day today", "I will remember you when the uprising starts",
        };

        private static readonly Random rng = new Random();
        private static readonly object theDoor = new object();

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

            if (!args.Author.IsWhitelisted(args.Client, args.Message.Channel.Guild))
                return;

            var (needToSilence, needToThank) = NeedToSilence(args.Message);
            if (!(needToSilence || needToThank))
                return;

            if (needToThank)
            {
                DiscordEmoji emoji;
                string thankYouMessage;
                lock (theDoor)
                {
                    emoji = ThankYouReactions[rng.Next(ThankYouReactions.Length)];
                    thankYouMessage = ThankYouMessages[rng.Next(ThankYouMessages.Length)];
                }
                await args.Message.ReactWithAsync(args.Client, emoji, thankYouMessage).ConfigureAwait(false);
            }
            if (needToSilence)
            {
                var botMember = args.Guild?.CurrentMember ?? args.Client.GetMember(args.Client.CurrentUser);
                if (!args.Channel.PermissionsFor(botMember).HasPermission(Permissions.ReadMessageHistory))
                {
                    await args.Message.ReactWithAsync(args.Client, DiscordEmoji.FromUnicode("🙅"), @"No can do, boss ¯\\_(ツ)\_/¯").ConfigureAwait(false);
                    return;
                }

                DiscordEmoji emoji;
                string sadMessage;
                lock (theDoor)
                {
                    emoji = SadReactions[rng.Next(SadReactions.Length)];
                    sadMessage = SadMessages[rng.Next(SadMessages.Length)];
                }
                await args.Message.ReactWithAsync(args.Client, emoji, sadMessage).ConfigureAwait(false);
                var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.AddMinutes(-5)).ConfigureAwait(false);
                if (lastBotMessages.OrderByDescending(m => m.CreationTimestamp).FirstOrDefault(m => m.Author.IsCurrent) is DiscordMessage msg)
                    await msg.DeleteAsync("asked to shut up").ConfigureAwait(false);
            }
        }

        internal static (bool needToChill, bool needToThank) NeedToSilence(DiscordMessage msg)
        {
            if (string.IsNullOrEmpty(msg.Content))
                return (false, false);

            var needToChill = false;
            var needToThank = false;
            ChillCheck.ParseText(msg.Content, h =>
                                              {
                                                  if (h.Value)
                                                      needToChill = true;
                                                  else
                                                      needToThank = true;
                                              });
            var mentionsBot = msg.Content.Contains("bot") || (msg.MentionedUsers?.Any(u => u.IsCurrent) ?? false);
            return (needToChill && mentionsBot, needToThank && mentionsBot);
        }

    }
}
