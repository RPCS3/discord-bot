using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using org.mariuszgromada.math.mxparser;

namespace CompatBot.Commands
{
    internal sealed class Misc: BaseCommandModuleCustom
    {
        private readonly Random rng = new Random();

        private static readonly List<string> EightBallAnswers = new List<string>
        {
            // 22
            "Ya fo sho", "Fo shizzle mah nizzle", "Yuuuup", "Da", "Affirmative", // 5
            "Sure", "Yeah, why not", "Most likely", "Sim", "Oui",
            "Heck yeah!", "Roger that", "Aye!", "Yes without a doubt m8!", "<:cell_ok_hand:324618647857397760>",
            "Don't be an idiot. YES.", "Mhm!", "Many Yes", "Yiss", "Sir, yes, Sir!", 
            "Yah!", "Ja",

            // 20
            "Maybe", "I don't know", "I don't care", "Who cares", "Maybe yes, maybe not",
            "Maybe not, maybe yes", "Ugh", "Probably", "Ask again later", "Error 404: answer not found",
            "Don't ask me that again", "You should think twice before asking", "You what now?", "Ask Neko", "Ask Ani",
            "Bloody hell, answering that ain't so easy", "I'm pretty sure that's illegal!", "What do *you* think?", "Only on Wednesdays", "Look in the mirror, you know the answer already",

            // 18
            "Nah mate", "Nope", "Njet", "Of course not", "Seriously no",
            "Noooooooooo", "Most likely not", "Não", "Non", "Hell no",
            "Absolutely not", "Nuh-uh!", "Nyet!", "Negatory!", "Heck no",
            "Nein!", "I think not", "I'm afraid not"
        };

        private static readonly List<string> RateAnswers = new List<string>
        {
            // 44
            "Not so bad", "I likesss!", "Pretty good", "Guchi gud", "Amazing!",
            "Glorious!", "Very good", "Excellent...", "Magnificent", "Rate bot says he likes, so you like too",
            "If you reorganize the words it says \"pretty cool\"", "I approve", "<:morgana_sparkle:315899996274688001>　やるじゃねーか！", "Not half bad 👍", "Belissimo!",
            "Cool. Cool cool cool", "I am in awe", "Incredible!", "Radiates gloriousness", "Like a breath of fresh air",
            "Sunshine for my digital soul 🌞", "Fantastic like petrichor 🌦", "Joyous like a rainbow 🌈", "Unbelievably good", "Can't recommend enough",
            "Not perfect, but ok", "So good!", "A lucky find!", "💯 approved", "I don't see any downsides",
            "Here's my seal of approval 💮", "As good as it gets", "A benchmark to pursue", "Should make you warm and fuzzy inside", "Fabulous",
            "Cool like a cup of good wine 🍷", "Magical ✨", "Wondrous like a unicorn 🦄", "Soothing sight for these tired eyes", "Lovely",
            "So cute!", "It's so nice, I think about it every day!", ":blush: Never expected to be this pretty!", "It's overflowing with charm!",

            // 20
            "Ask MsLow", "Could be worse", "I need more time to think about it", "It's ok, nothing and no one is perfect", "🆗",
            "You already know, my boi", "Unexpected like a bouquet of sunflowers 🌻", "Hard to measure precisely...", "Requires more data to analyze", "Passable",
            "Quite unique 🤔", "Less like an orange, and more like an apple", "I don't know, man...", "It is so tiring to grade everything...", "...",
            "Bland like porridge", "🤔", "Ok-ish?", "Not _bad_, but also not _good_", "Why would you want to _rate_ this?",

            // 26
            "Bad", "Very bad", "Pretty bad", "Horrible", "Ugly",
            "Disgusting", "Literally the worst", "Not interesting", "Simply ugh", "I don't like it! You shouldn't either!",
            "Just like you, 💩", "Not approved", "Big Mistake", "The opposite of good", "Could be better",
            "🤮", "😐",  "So-so", "Not worth it", "Mediocre at best",
            "Useless", "I think you misspelled `poop` there", "Nothing special", "😔", "Real shame",
            "Boooooooo!"
        };

        private static readonly HashSet<string> Me = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "I", "me", "myself", "moi"
        };

        private static readonly HashSet<char> Vowels = new HashSet<char> {'a', 'e', 'i', 'o', 'u'};

        [Command("credits"), Aliases("about")]
        [Description("Author Credit")]
        public async Task Credits(CommandContext ctx)
        {
            DiscordEmoji hcorion;
            try
            {
                hcorion = DiscordEmoji.FromName(ctx.Client, ":hcorion:");
            }
            catch
            {
                hcorion = DiscordEmoji.FromUnicode("🍁");
            }
            var embed = new DiscordEmbedBuilder
                {
                    Title = "RPCS3 Compatibility Bot",
                    Url = "https://github.com/RPCS3/discord-bot",
                    Color = DiscordColor.Purple,
                }.AddField("Made by",
                    "💮 13xforever\n" +
                    "🇭🇷 Roberto Anić Banić aka nicba1010")
                .AddField("People who ~~broke~~ helped test the bot",
                    "🐱 Juhn\n" +
                    $"{hcorion} hcorion\n" +
                    "🙃 TGE");
            await ctx.RespondAsync(embed: embed.Build());
        }

        [Command("math"), TriggersTyping]
        [Description("Math, here you go Juhn")]
        public async Task Math(CommandContext ctx, [RemainingText, Description("Math expression")] string expression)
        {
            var result = @"Something went wrong ¯\\_(ツ)\_/¯" + "\nMath is hard, yo";
            try
            {
                var expr = new Expression(expression);
                result = expr.calculate().ToString();
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Math failed");
            }
            await ctx.RespondAsync(result).ConfigureAwait(false);
        }
 
        [Command("roll")]
        [Description("Generates a random number between 1 and maxValue. Can also roll dices like `2d6`. Default is 1d6")]
        public async Task Roll(CommandContext ctx, [Description("Some positive natural number")] int maxValue = 6, [Description("Optional text"), RemainingText] string comment = null)
        {
            string result = null;
            if (maxValue > 1)
                    lock (rng) result = (rng.Next(maxValue) + 1).ToString();
            if (string.IsNullOrEmpty(result))
                await ctx.ReactWithAsync(DiscordEmoji.FromUnicode("💩"), $"How is {maxValue} a positive natural number?").ConfigureAwait(false);
            else
                await ctx.RespondAsync(result).ConfigureAwait(false);
        }

        [Command("roll")]
        public async Task Roll(CommandContext ctx, [Description("Dices to roll (i.e. 2d6 for two 6-sided dices)")] string dices, [Description("Optional text"), RemainingText] string comment = null)
        {
            var result = "";
            if (dices is string dice && Regex.IsMatch(dice, @"\d+d\d+"))
            {
                await ctx.TriggerTypingAsync().ConfigureAwait(false);
                var diceParts = dice.Split('d', StringSplitOptions.RemoveEmptyEntries);
                if (int.TryParse(diceParts[0], out var num) && int.TryParse(diceParts[1], out var face) &&
                    0 < num && num < 101 &&
                    1 < face && face < 1001)
                {
                    List<int> rolls;
                    lock (rng) rolls = Enumerable.Range(0, num).Select(_ => rng.Next(face) + 1).ToList();
                    if (rolls.Count > 1)
                    {
                        result = "Total: " + rolls.Sum();
                        result += "\nRolls: " + string.Join(' ', rolls);
                    }
                    else
                        result = rolls.Sum().ToString();
                }
            }
            if (string.IsNullOrEmpty(result))
                await ctx.ReactWithAsync(DiscordEmoji.FromUnicode("💩"), "Invalid dice description passed").ConfigureAwait(false);
            else
                await ctx.RespondAsync(result).ConfigureAwait(false);
        }

        [Command("8ball"), Cooldown(20, 60, CooldownBucketType.Channel)]
        [Description("Provides a ~~random~~ objectively best answer to your question")]
        public async Task EightBall(CommandContext ctx, [RemainingText, Description("A yes/no question")] string question)
        {
            string answer;
            lock (rng)
                answer = EightBallAnswers[rng.Next(EightBallAnswers.Count)];
            await ctx.RespondAsync(answer).ConfigureAwait(false);
        }

        [Command("rate"), Cooldown(20, 60, CooldownBucketType.Channel)]
        [Description("Gives a ~~random~~ expert judgment on the matter at hand")]
        public async Task Rate(CommandContext ctx, [RemainingText, Description("Something to rate")] string whatever)
        {
            var choices = RateAnswers;
            whatever = whatever.ToLowerInvariant().Trim();
            if (whatever.Contains("my", StringComparison.InvariantCultureIgnoreCase))
                whatever = string.Join(" ", whatever.Split(' ').Select(p => p.Equals("my", StringComparison.InvariantCultureIgnoreCase) ? $"<@{ctx.Message.Author.Id}>'s" : p));
            try
            {
                var whateverParts = whatever.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                var prefix = DateTime.UtcNow.ToString("yyyyMMddHH");
                var nekoNick = await ctx.GetUserNameAsync(272032356922032139ul, null, "Nekotekina").ConfigureAwait(false);
                var kdNick = await ctx.GetUserNameAsync(272631898877198337, null, "kd-11").ConfigureAwait(false);
                if (whatever is string neko && (neko.Contains("272032356922032139") || neko.Contains("neko") || neko.Contains(nekoNick)))
                {
                    choices = RateAnswers.Concat(Enumerable.Repeat("Ugh", RateAnswers.Count * 3)).ToList();
                    if (await new DiscordUserConverter().ConvertAsync("272032356922032139", ctx).ConfigureAwait(false) is Optional<DiscordUser> user && user.HasValue)
                        whatever = user.Value.Id.ToString();
                }
                else if (whatever is string kd && (kd.Contains("272631898877198337") || kd.Contains("kd-11") || kd.Contains("kd11") || kd.Contains(kdNick) || whateverParts.Any(p => p == "kd")))
                {
                    choices = RateAnswers.Concat(Enumerable.Repeat("RSX genius", RateAnswers.Count * 3)).ToList();
                    if (await new DiscordUserConverter().ConvertAsync("272631898877198337", ctx).ConfigureAwait(false) is Optional<DiscordUser> user && user.HasValue)
                        whatever = user.Value.Id.ToString();
                }
                else if (whatever is string sonic && (sonic == "sonic" || sonic.Contains("sonic the")))
                {
                    choices = RateAnswers.Concat(Enumerable.Repeat("💩 out of 🦔", RateAnswers.Count)).Concat(new[] {"Sonic out of 🦔", "Sonic out of 10"}).ToList();
                    whatever = "sonic";
                }
                else if (whateverParts.Length == 1)
                {
                    DiscordUser u = null;
                    if (Me.Contains(whateverParts[0]))
                    {
                        whatever = ctx.Message.Author.Id.ToString();
                        u = ctx.Message.Author;
                    }
                    else if (await new DiscordUserConverter().ConvertAsync(whateverParts[0], ctx).ConfigureAwait(false) is Optional<DiscordUser> user && user.HasValue)
                    {
                        whatever = user.Value.Id.ToString();
                        u = user.Value;
                    }
                    if (u != null)
                    {
                        var roles = ctx.Client.GetMember(u)?.Roles.ToList();
                        if (roles?.Count > 0)
                        {

                            var role = roles[new Random((prefix + u.Id).GetHashCode()).Next(roles.Count)].Name?.ToLowerInvariant();
                            if (!string.IsNullOrEmpty(role))
                            {
                                if (role.EndsWith('s'))
                                    role = role.Substring(0, role.Length - 1);
                                var article = Vowels.Contains(role[0]) ? "n" : "";
                                choices = RateAnswers.Concat(Enumerable.Repeat($"Pretty fly for a{article} {role} guy", RateAnswers.Count / 20)).ToList();
                            }
                        }
                    }
                }
                whatever = prefix + whatever?.Trim();
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to rate {whatever}");
            }
            var seed = whatever.GetHashCode(StringComparison.CurrentCultureIgnoreCase);
            var seededRng = new Random(seed);
            var answer = choices[seededRng.Next(choices.Count)];
            await ctx.RespondAsync(answer).ConfigureAwait(false);
        }
    }
}
