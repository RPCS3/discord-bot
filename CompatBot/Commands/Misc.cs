using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;

namespace CompatBot.Commands
{
    internal sealed class Misc: BaseCommandModuleCustom
    {
        private readonly Random rng = new Random();

        private static readonly List<string> EightBallAnswers = new List<string>
        {
            // 24
            "Ya fo sho", "Fo shizzle mah nizzle", "Yuuuup", "Da", "Affirmative", // 5
            "Sure", "Yeah, why not", "Most likely", "Sim", "Oui",
            "Heck yeah!", "Roger that", "Aye!", "Yes without a doubt m8!", "<:cell_ok_hand:324618647857397760>",
            "Don't be an idiot. YES.", "Mhm!", "Many Yes", "Yiss", "Sir, yes, Sir!", 
            "Yah!", "Ja", "Umu!", "Make it so", 

            // 20
            "Maybe", "I don't know", "I don't care", "Who cares", "Maybe yes, maybe not",
            "Maybe not, maybe yes", "Ugh", "Probably", "Ask again later", "Error 404: answer not found",
            "Don't ask me that again", "You should think twice before asking", "You what now?", "Ask Neko", "Ask Ani",
            "Bloody hell, answering that ain't so easy", "I'm pretty sure that's illegal!", "What do *you* think?", "Only on Wednesdays", "Look in the mirror, you know the answer already",

            // 18
            "Nah mate", "Nope", "Njet", "Of course not", "Seriously no",
            "Noooooooooo", "Most likely not", "Não", "Non", "Hell no",
            "Absolutely not", "Nuh-uh!", "Nyet!", "Negatory!", "Heck no",
            "Nein!", "I think not", "I'm afraid not",
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
            "So cute!", "It's so nice, I think about it every day!", "😊 Never expected to be this pretty!", "It's overflowing with charm!",

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
            "Boooooooo!",
        };

        private static readonly char[] Separators = { ' ', '　', '\r', '\n' };
        private static readonly char[] Suffixes = {',', '.', ':', ';', '!', '?', ')', '}', ']', '>', '+', '-', '/', '*', '=', '"', '\'', '`'};
        private static readonly char[] Prefixes = {'@', '(', '{', '[', '<', '!', '`', '"', '\'', '#'};
        private static readonly char[] EveryTimable = Separators.Concat(Suffixes).Concat(Prefixes).Distinct().ToArray();

        private static readonly HashSet<string> Me = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "I", "me", "myself", "moi"
        };

        private static readonly HashSet<string> Your = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "your", "you're", "yor", "ur", "yours", "your's",
        };

        private static readonly HashSet<char> Vowels = new HashSet<char> {'a', 'e', 'i', 'o', 'u'};

        private static readonly Regex Instead = new Regex("rate (?<instead>.+) instead", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);

        [Command("credits"), Aliases("about")]
        [Description("Author Credit")]
        public async Task About(CommandContext ctx)
        {
            var hcorion = ctx.Client.GetEmoji(":hcorion:", DiscordEmoji.FromUnicode("🍁"));
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
                    "🙃 TGE\n" +
                    "🍒 Maru\n" +
                    "♋ Tourghool");
            await ctx.RespondAsync(embed: embed.Build());
        }

        [Command("roll")]
        [Description("Generates a random number between 1 and maxValue. Can also roll dices like `2d6`. Default is 1d6")]
        public async Task Roll(CommandContext ctx, [Description("Some positive natural number")] int maxValue = 6, [RemainingText, Description("Optional text")] string comment = null)
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
        public async Task Roll(CommandContext ctx, [RemainingText, Description("Dices to roll (i.e. 2d6+1 for two 6-sided dices with a bonus 1)")] string dices)
        {
            var result = "";
            var embed = new DiscordEmbedBuilder();
            if (dices is string dice && Regex.Matches(dice, @"(?<num>\d+)?d(?<face>\d+)(?:\+(?<mod>\d+))?") is MatchCollection matches && matches.Count > 0 && matches.Count <= EmbedPager.MaxFields)
            {
                var grandTotal = 0;
                foreach (Match m in matches)
                {
                    result = "";
                    if (!int.TryParse(m.Groups["num"].Value, out var num))
                        num = 1;
                    if (int.TryParse(m.Groups["face"].Value, out var face)
                        && 0 < num && num < 101
                        && 1 < face && face < 1001)
                    {
                        List<int> rolls;
                        lock (rng) rolls = Enumerable.Range(0, num).Select(_ => rng.Next(face) + 1).ToList();
                        var total = rolls.Sum();
                        var totalStr = total.ToString();
                        int.TryParse(m.Groups["mod"].Value, out var mod);
                        if (mod > 0)
                            totalStr += $" + {mod} = {total + mod}";
                        var rollsStr = string.Join(' ', rolls);
                        if (rolls.Count > 1)
                        {
                            result = "Total: " + totalStr;
                            result += "\nRolls: " + rollsStr;
                        }
                        else
                            result = totalStr;
                        grandTotal += total + mod;
                        var diceDesc = $"{num}d{face}";
                        if (mod > 0)
                            diceDesc += "+" + mod;
                        embed.AddField(diceDesc, result.Trim(EmbedPager.MaxFieldLength), true);
                    }
                }
                if (matches.Count == 1)
                    embed = null;
                else
                {
                    embed.Description = "Grand total: " + grandTotal;
                    embed.Title = $"Result of {matches.Count} dice rolls";
                    embed.Color = Config.Colors.Help;
                    result = null;
                }
            }
            else
            {
                await Roll(ctx).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrEmpty(result) && embed == null)
                await ctx.ReactWithAsync(DiscordEmoji.FromUnicode("💩"), "Invalid dice description passed").ConfigureAwait(false);
            else if (embed != null)
                await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
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
        public async Task Rate(CommandContext ctx, [RemainingText, Description("Something to rate")] string whatever = "")
        {
            try
            {
                var choices = RateAnswers;
                var choiceFlags = new HashSet<char>();
                whatever = whatever.ToLowerInvariant();
                var matches = Instead.Matches(whatever);
                if (matches.Any())
                {
                    var insteadWhatever = matches.Last().Groups["instead"].Value.TrimEager();
                    if (!string.IsNullOrEmpty(insteadWhatever))
                        whatever = insteadWhatever;
                }
                foreach (var attachment in ctx.Message.Attachments)
                    whatever += $" {attachment.FileSize}";

                var nekoUser = await ctx.Client.GetUserAsync(272032356922032139ul).ConfigureAwait(false);
                var nekoMember = ctx.Client.GetMember(ctx.Guild, nekoUser);
                var nekoMatch = new HashSet<string>(new[] {nekoUser.Id.ToString(), nekoUser.Username, nekoMember?.DisplayName ?? "neko", "neko", "nekotekina",});
                var kdUser = await ctx.Client.GetUserAsync(272631898877198337ul).ConfigureAwait(false);
                var kdMember = ctx.Client.GetMember(ctx.Guild, kdUser);
                var kdMatch = new HashSet<string>(new[] {kdUser.Id.ToString(), kdUser.Username, kdMember?.DisplayName ?? "kd-11", "kd", "kd-11", "kd11", });
                var botMember = ctx.Client.GetMember(ctx.Client.CurrentUser);
                var botMatch = new HashSet<string>(new[] {botMember.Id.ToString(), botMember.Username, botMember.DisplayName, "yourself", "urself", "yoself",});

                var prefix = DateTime.UtcNow.ToString("yyyyMMddHH");
                var words = whatever.Split(Separators);
                var result = new StringBuilder();
                for (var i = 0; i < words.Length; i++)
                {
                    var word = words[i].TrimEager();
                    var suffix = "";
                    var tmp = word.TrimEnd(Suffixes);
                    if (tmp.Length != word.Length)
                    {
                        suffix = word.Substring(tmp.Length);
                        word = tmp;
                    }
                    tmp = word.TrimStart(Prefixes);
                    if (tmp.Length != word.Length)
                    {
                        result.Append(word.Substring(0, word.Length - tmp.Length));
                        word = tmp;
                    }
                    if (word.EndsWith("'s"))
                    {
                        suffix = "'s" + suffix;
                        word = word.Substring(0, word.Length - 2);
                    }

                    void MakeCustomRoleRating(DiscordMember m)
                    {
                        if (m != null && !choiceFlags.Contains('f'))
                        {
                            var roleList = m.Roles.ToList();
                            if (roleList.Any())
                            {

                                var role = roleList[new Random((prefix + m.Id).GetHashCode()).Next(roleList.Count)].Name?.ToLowerInvariant();
                                if (!string.IsNullOrEmpty(role))
                                {
                                    if (role.EndsWith('s'))
                                        role = role.Substring(0, role.Length - 1);
                                    var article = Vowels.Contains(role[0]) ? "n" : "";
                                    choices = RateAnswers.Concat(Enumerable.Repeat($"Pretty fly for a{article} {role} guy", RateAnswers.Count / 20)).ToList();
                                    choiceFlags.Add('f');
                                }
                            }
                        }
                    }

                    if (Me.Contains(word))
                        result.Append(ctx.Message.Author.Mention);
                    else if (word == "my")
                        result.Append(ctx.Message.Author.Mention).Append("'s");
                    else if (botMatch.Contains(word))
                        result.Append(ctx.Client.CurrentUser.Mention);
                    else if (Your.Contains(word))
                        result.Append(ctx.Client.CurrentUser.Mention).Append("'s");
                    else if (word.StartsWith("actually") || word.StartsWith("nevermind") || word.StartsWith("nvm"))
                        result.Clear();
                    else if (i == 0 && await ctx.ResolveMemberAsync(word).ConfigureAwait(false) is DiscordMember m)
                    {
                        if (suffix.Length == 0)
                            MakeCustomRoleRating(m);
                        result.Append(m.Mention);
                    }
                    else if (nekoMatch.Contains(word))
                    {
                        if (i == 0 && suffix.Length == 0)
                        {
                            choices = RateAnswers.Concat(Enumerable.Repeat("Ugh", RateAnswers.Count * 3)).ToList();
                            MakeCustomRoleRating(nekoMember);
                        }
                        result.Append(nekoUser.Mention);
                    }
                    else if (kdMatch.Contains(word))
                    {
                        if (i == 0 && suffix.Length == 0)
                        {
                            choices = RateAnswers.Concat(Enumerable.Repeat("RSX genius", RateAnswers.Count * 3)).ToList();
                            MakeCustomRoleRating(kdMember);
                        }
                        result.Append(kdUser.Mention);
                    }
                    else
                        result.Append(word);
                    result.Append(suffix).Append(" ");
                }
                whatever = result.ToString();
                var cutIdx = whatever.LastIndexOf("never mind");
                if (cutIdx > -1)
                    whatever = whatever.Substring(cutIdx);
                whatever = whatever.Replace("'s's", "'s").TrimStart(EveryTimable).Trim();
                if (whatever.StartsWith("rate "))
                    whatever = whatever.Substring("rate ".Length);
                if (whatever == "sonic" || whatever.Contains("sonic the"))
                    choices = RateAnswers.Concat(Enumerable.Repeat("💩 out of 🦔", RateAnswers.Count)).Concat(new[] {"Sonic out of 🦔", "Sonic out of 10"}).ToList();

                if (string.IsNullOrEmpty(whatever))
                    await ctx.RespondAsync("Rating nothing makes _**so much**_ sense, right?").ConfigureAwait(false);
                else
                {
                    var seed = (prefix + whatever).GetHashCode(StringComparison.CurrentCultureIgnoreCase);
                    var seededRng = new Random(seed);
                    var answer = choices[seededRng.Next(choices.Count)];
                    await ctx.RespondAsync(answer).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to rate {whatever}");
            }
        }

        [Command("meme"), Aliases("memes"), Cooldown(1, 30, CooldownBucketType.Channel), Hidden]
        [Description("No, memes are not implemented yet")]
        public async Task Memes(CommandContext ctx, [RemainingText] string _ = null)
        {
            var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            await ch.SendMessageAsync($"{ctx.User.Mention} congratulations, you're the meme").ConfigureAwait(false);
        }

        [Command("download"), Cooldown(1, 10, CooldownBucketType.Channel)]
        [Description("Find games to download")]
        public Task Download(CommandContext ctx, [RemainingText] string game)
            => Psn.SearchForGame(ctx, game);
    }
}
