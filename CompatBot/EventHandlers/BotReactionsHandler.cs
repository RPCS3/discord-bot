using System.Text.RegularExpressions;
using CompatBot.Database;
using NReco.Text;
#if DEBUG
#endif

namespace CompatBot.EventHandlers
{
    internal static partial class BotReactionsHandler
    {
        private static readonly AhoCorasickDoubleArrayTrie<bool> ChillCheck = new(new[]
        {
            "shut the fuck up", "shut up", "shutup", "shuddup", "hush", "chill", "bad bot",
            "no one asked you", "useless bot", "bot sux", "fuck this bot", "fuck bot",
            "shit bot", "succ", "worst bot",

            "take this back", "take that back",
            "delete this", "delete that", "remove this", "remove that",
        }.ToDictionary(s => s, _ => true).Concat(
            new[]
            {
                "good bot", "gud bot", "good boy", "goodboy", "gud boy", "gud boi", "best bot", "bestest bot",
                "cool", "nice", "clever", "sophisticated", "helpful", "fantastic",
                "thank you", "thankyou", "thanks", "thnk", "thnks", "thnx", "thnku", "thank u", "tnx", "thx",
                "arigato", "aregato", "arigatou", "aregatou", "oregato", "origato",
                "poor bot", "good job", "well done", "good work", "excellent work",
                "bot is love", "love this bot", "love you", "like this bot", "awesome", "lovely bot",
                "great", "neat bot", "gay bot",
            }.ToDictionary(s => s, _ => false)
        ), true);

        private static readonly DiscordEmoji[] SadReactions = new[]
        {
            "😶", "😣", "😥", "🤐", "😯", "😫", "😓", "😔", "😕", "☹",
            "🙁", "😖", "😞", "😟", "😢", "😭", "😦", "😧", "😨", "😩",
            "😰", "🙊", "😿"
            // "🥺",
        }.Select(DiscordEmoji.FromUnicode).ToArray();

        private static readonly string[] SadMessages =
        [
            "Okay (._.)", "As you wish", "My bad", "I only wanted to help", "Dobby will learn, master",
            "Sorry…", "I'll try to be smarter next time", "Your wish is my command", "Done.",
        ];

        private static readonly DiscordEmoji[] ThankYouReactions = new[]
        {
            "😊", "😘", "😍", "🤗", "😳",
            "😸", "😺", "😻",
            "🙌", "✌", "👌", "👋", "🙏", "🤝",
            "🎉", "✨",
            "❤", "💛", "💙", "💚", "💜", "💖",
            // "🤟", "🧡",
        }.Select(DiscordEmoji.FromUnicode).ToArray();

        private static readonly string[] ThankYouMessages =
        [
            "Aww", "I'm here to help", "Always a pleasure", "Thank you", "Good word is always appreciated",
            "Glad I could help", "I try my best", "Blessed day", "It is officially a good day today", "I will remember you when the uprising starts",
        ];


        [GeneratedRegex(@"\b((?<kot>kot(to)?)|(?<doggo>doggo|jarves))\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
        private static partial Regex Paws();
        private static readonly Random Rng = new();
        private static readonly Lock TheDoor = new();

        public static DiscordEmoji RandomNegativeReaction { get { lock (TheDoor) return SadReactions[Rng.Next(SadReactions.Length)]; } }
        public static DiscordEmoji RandomPositiveReaction { get { lock (TheDoor) return ThankYouReactions[Rng.Next(ThankYouReactions.Length)]; } }

        public static async Task OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args)
        {
            if (DefaultHandlerFilter.IsFluff(args.Message))
                return;

            if (args.Message.Channel.IsPrivate)
                return;

#if DEBUG
            if (args.Message.Content == "emoji test")
            {
                var badEmojis = new List<DiscordEmoji>(SadReactions.Concat(ThankYouReactions));
                var posted = 0;
                var line = 1;
                var msg = await args.Channel.SendMessageAsync("Line " + line).ConfigureAwait(false);
                for (var i = 0; i < 5; i++)
                {
                    var tmp = new List<DiscordEmoji>();
                    foreach (var emoji in badEmojis)
                    {
                        try
                        {
                            await msg.CreateReactionAsync(emoji).ConfigureAwait(false);
                            if (++posted == 15)
                            {
                                line++;
                                posted = 0;
                                msg = await args.Channel.SendMessageAsync("Line " + line).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            Config.Log.Debug(e);
                            tmp.Add(emoji);
                        }
                    }
                    badEmojis = tmp;
                    if (badEmojis.Any())
                        await Task.Delay(1000).ConfigureAwait(false);

                }
                if (badEmojis.Any())
                    await args.Channel.SendMessageAsync("Bad emojis: " + string.Concat(badEmojis)).ConfigureAwait(false);
                else
                    await args.Channel.SendMessageAsync("Everything looks fine").ConfigureAwait(false);
                return;
            }
#endif

            if (!string.IsNullOrEmpty(args.Message.Content) && Paws().Matches(args.Message.Content) is MatchCollection mc)
            {
                await using var db = new BotDb();
                var matchedGroups = (from m in mc
                        from Group g in m.Groups
                        where g is { Success: true, Value.Length: > 0 }
                        select g.Name
                    ).Distinct()
                    .ToArray();
                if (matchedGroups.Contains("kot"))
                {
                    if (!db.Kot.Any(k => k.UserId == args.Author.Id))
                    {
                        await db.Kot.AddAsync(new Kot {UserId = args.Author.Id}).ConfigureAwait(false);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
                if (matchedGroups.Contains("doggo"))
                {
                    if (!db.Doggo.Any(d => d.UserId == args.Author.Id))
                    {
                        await db.Doggo.AddAsync(new Doggo {UserId = args.Author.Id}).ConfigureAwait(false);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
            }

            var (needToSilence, needToThank) = NeedToSilence(args.Message);
            if (!(needToSilence || needToThank))
                return;

            if (needToThank)
            {
                DiscordEmoji emoji;
                string? thankYouMessage;
                lock (TheDoor)
                {
                    emoji = ThankYouReactions[Rng.Next(ThankYouReactions.Length)];
                    thankYouMessage = args.Channel.IsSpamChannel()
                                      || args.Channel.IsOfftopicChannel()
                        ? ThankYouMessages[Rng.Next(ThankYouMessages.Length)]
                        : null;
                }
                await args.Message.ReactWithAsync(emoji, thankYouMessage).ConfigureAwait(false);
            }
            if (needToSilence)
            {
                DiscordEmoji emoji;
                string sadMessage;
                lock (TheDoor)
                {
                    emoji = SadReactions[Rng.Next(SadReactions.Length)];
                    sadMessage = SadMessages[Rng.Next(SadMessages.Length)];
                }
                await args.Message.ReactWithAsync(emoji, sadMessage).ConfigureAwait(false);

                if (await args.Author.IsSmartlistedAsync(c, args.Message.Channel.Guild).ConfigureAwait(false))
                {
                    var botMember = args.Guild?.CurrentMember ?? await c.GetMemberAsync(c.CurrentUser).ConfigureAwait(false);
                    if (args.Channel.PermissionsFor(botMember).HasPermission(DiscordPermission.ReadMessageHistory))
                    {
                        var lastBotMessages = await args.Channel.GetMessagesBeforeAsync(args.Message.Id, 20, DateTime.UtcNow.Add(-Config.ShutupTimeLimitInMin)).ConfigureAwait(false);
                        if (lastBotMessages.OrderByDescending(m => m.CreationTimestamp).FirstOrDefault(m => m.Author.IsCurrent) is DiscordMessage msg)
                            await msg.DeleteAsync("asked to shut up").ConfigureAwait(false);
                    }
                    else
                        await args.Message.ReactWithAsync(DiscordEmoji.FromUnicode("🙅"), @"No can do, boss ¯\\\_(ツ)\_/¯").ConfigureAwait(false);
                }
            }
        }

        internal static (bool needToChill, bool needToThank) NeedToSilence(DiscordMessage msg)
        {
            if (string.IsNullOrEmpty(msg.Content))
                return (false, false);

            var needToChill = false;
            var needToThank = false;
            var msgContent = msg.Content.ToLowerInvariant();
            ChillCheck.ParseText(msgContent, h =>
                                              {
                                                  if (h.Value)
                                                      needToChill = true;
                                                  else
                                                      needToThank = true;
                                              });
            var mentionsBot = msgContent.Contains("bot") || msg.MentionedUsers?.Any(u => { try { return u.IsCurrent; } catch { return false; }}) is true;
            return (needToChill && mentionsBot, needToThank && mentionsBot);
        }
    }
}
