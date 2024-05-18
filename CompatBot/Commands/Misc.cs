﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using HomoglyphConverter;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal sealed partial class Misc: BaseCommandModuleCustom
{
    private static readonly Random rng = new();

    private static readonly List<string> EightBallAnswers =
    [
        // keep this at 2:1:1 ratio 
        // 70
        "It is certain", "It is decidedly so", "Without a doubt", "Yes definitely", "You may rely on it",
        "As I see it, yes", "Most likely", "Outlook good", "Yes", "Signs point to yes", // 10
        "Ya fo sho", "Fo shizzle mah nizzle", "Yuuuup", "Da", "Affirmative",
        "Sure", "Yeah, why not", "Sim", "Oui", "Crystal ball says yes", // 20
        "Heck yeah!", "Roger that", "Aye!", "Yes without a doubt m8!", ":cell_ok_hand_hd:",
        "Don't be an idiot. YES.", "Mhm!", "Many Yes", "Yiss", "Sir, yes, Sir!", // 30 
        "Yah!", "Ja", "Umu!", "Make it so", "Sure thing",
        "Certainly", "Of course", "Definitely", "Indeed", "Much yes", // 40
        "Consider it done", "Totally", "You bet", "Yup", "Yep",
        "Positive!", "Yarp", "Hmmm, yes!", "That's a yes for me", "Aye mate", // 50
        "Absolutely", "Totes my goats", "Without fail", "👌", "👍",
        "Sí", "Sí, señor", "Sì", "Sì, signore", "The wheel of fate is already turning", // 60
        "It's not a no", "Very likely", "Undoubtedly so", "That's a positive", "Yes, you silly",
        "Bones said yes", "Tea leaves settled in a 'yes' pattern", "Dice roll was solid, so yes", "No doubt about it", "Hmmm, I think so", // 70

        // 30
        "Reply hazy, try again", "Ask again later", "Better not tell you now", "Cannot predict now", "Concentrate and ask again",
        "Maybe", "I don't know", "I don't care", "Who cares", "Maybe yes, maybe not", // 10
        "Maybe not, maybe yes", "Ugh", "Probably", "Error 404: answer not found", "Crystal ball is cloudy as milk, ask later",
        "Don't ask me that again", "You should think twice before asking", "You what now?", "Ask Neko", "Ask Ani", // 20
        "Bloody hell, answering that ain't so easy", "I'm pretty sure that's illegal!", "What do *you* think?", "Only on Wednesdays", "Look in the mirror, you know the answer already",
        "Don't know, don't care", "_shows signs of complete confusion_", "Have you googled it?", "Not sure my dude", "🤔", // 30

        // 40
        "Don't count on it", "My reply is no", "My sources say no", "Outlook not so good", "Very doubtful",
        "Nah mate", "Nope", "Njet", "Of course not", "Seriously no", // 10
        "Noooooooooo", "Most likely not", "Não", "Non", "Hell no",
        "Absolutely not", "Nuh-uh!", "Nyet!", "Negatory!", "Heck no", // 20
        "Nein!", "I think not", "I'm afraid not", "Nay", "Yesn't",
        "No way", "Certainly not", "I must say no", "Nah", "Negative", // 30
        "Definitely not", "No way, Jose", "Not today", "No no no no no no no no no no. No.", "Not in a million years",
        "I'm afraid I can't let you do that Dave.", "This mission is too important for me to allow you to jeopardize it.", "Oh, I don't think so", "By *no* means", "👎" // 40
    ];

    private static readonly List<string> EightBallSnarkyComments =
    [
        "Can't answer the question that wasn't asked",
        "Having issues with my mind reading attachment, you'll have to state your question explicitly",
        "Bad reception on your brain waves today, can't read the question",
        "What should the answer be for the question that wasn't asked 🤔",
        "In Discord no one can read your question if you don't type it",
        "In space no one can hear you scream; that's what you're doing right now",
        "Unfortunately there's no technology to transmit your question telepathically just yet",
        "I'd say maybe, but I'd need to see your question first"
    ];

    private static readonly List<string> EightBallTimeUnits =
    [
        "second", "minute", "hour", "day", "week", "month", "year", "decade", "century", "millennium",
        "night", "moon cycle", "solar eclipse", "blood moon", "complete emulator rewrite"
    ];

    private static readonly List<string> RateAnswers =
    [
        "Not so bad", "I likesss!", "Pretty good", "Guchi gud", "Amazing!",
        "Glorious!", "Very good", "Excellent...", "Magnificent", "Rate bot says he likes, so you like too",
        "If you reorganize the words it says \"pretty cool\"", "I approve", "<:morgana_sparkle:315899996274688001>　やるじゃねーか！", "Not half bad 👍", "Belissimo!",
        "Cool. Cool cool cool", "I am in awe", "Incredible!", "Radiates gloriousness", "Like a breath of fresh air",
        "Sunshine for my digital soul 🌞", "Fantastic like petrichor 🌦", "Joyous like a rainbow 🌈", "Unbelievably good", "Can't recommend enough",
        "Not perfect, but ok", "So good!", "A lucky find!", "💯 approved", "I don't see any downsides",
        "Here's my seal of approval 💮", "As good as it gets", "A benchmark to pursue", "Should make you warm and fuzzy inside", "Fabulous",
        "Cool like a cup of good wine 🍷", "Magical ✨", "Wondrous like a unicorn 🦄", "Soothing sight for these tired eyes", "Lovely",
        "So cute!", "It's so nice, I think about it every day!", "😊 Never expected to be this pretty!", "It's overflowing with charm!", "Filled with passion!",
        "A love magnet", "Pretty Fancy", "Admirable", "Sweet as a candy", "Delightful",
        "Enchanting as the Sunset", "A beacon of hope!", "Filled with hope!", "Shiny!", "Absolute Hope!",
        "The meaning of hope", "Inspiring!", "Marvelous", "Breathtaking", "Better than bubble wrap.",

        // 22
        "Ask MsLow", "Could be worse", "I need more time to think about it", "It's ok, nothing and no one is perfect", "🆗",
        "You already know, my boi", "Unexpected like a bouquet of sunflowers 🌻", "Hard to measure precisely...", "Requires more data to analyze", "Passable",
        "Quite unique 🤔", "Less like an orange, and more like an apple", "I don't know, man...", "It is so tiring to grade everything...", "...",
        "Bland like porridge", "🤔", "Ok-ish?", "Not _bad_, but also not _good_", "Why would you want to _rate_ this?", "meh",
        "I've seen worse",

        // 43
        "Bad", "Very bad", "Pretty bad", "Horrible", "Ugly",
        "Disgusting", "Literally the worst", "Not interesting", "Simply ugh", "I don't like it! You shouldn't either!",
        "Just like you, 💩", "Not approved", "Big Mistake", "The opposite of good", "Could be better",
        "🤮", "😐", "So-so", "Not worth it", "Mediocre at best",
        "Useless", "I think you misspelled `poop` there", "Nothing special", "😔", "Real shame",
        "Boooooooo!", "Poopy", "Smelly", "Feeling-breaker", "Annoying",
        "Boring", "Easily forgettable", "An Abomination", "A Monstrosity", "Truly horrific",
        "Filled with despair!", "Eroded by despair", "Hopeless…", "It's pretty foolish to want to rate this", "Cursed with misfortune",
        "Nothing but terror", "Not good, at all", "A waste of time",
    ];

    private static readonly char[] Separators = [' ', '　', '\r', '\n'];
    private static readonly char[] Suffixes = [',', '.', ':', ';', '!', '?', ')', '}', ']', '>', '+', '-', '/', '*', '=', '"', '\'', '`'];
    private static readonly char[] Prefixes = ['@', '(', '{', '[', '<', '!', '`', '"', '\'', '#'];
    private static readonly char[] EveryTimable = Separators.Concat(Suffixes).Concat(Prefixes).Distinct().ToArray();

    private static readonly HashSet<string> Me = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "I", "me", "myself", "moi"
    };

    private static readonly HashSet<string> Your = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "your", "you're", "yor", "ur", "yours", "your's",
    };

    private static readonly HashSet<char> Vowels = ['a', 'e', 'i', 'o', 'u'];

    [GeneratedRegex("rate (?<instead>.+) instead", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex Instead();
    [GeneratedRegex(@"(?<num>\d+)?d(?<face>\d+)(?:\+(?<mod>\d+))?")]
    private static partial Regex DiceNotationPattern();

    [Command("roll")]
    [Description("Generates a random number between 1 and maxValue. Can also roll dices like `2d6`. Default is 1d6")]
    public Task Roll(CommandContext ctx, [Description("Some positive natural number")] int maxValue = 6, [RemainingText, Description("Optional text")] string? comment = null)
        => RollImpl(ctx.Message, maxValue);

    [Command("roll")]
    public Task Roll(CommandContext ctx, [RemainingText, Description("Dices to roll (i.e. 2d6+1 for two 6-sided dices with a bonus 1)")] string dices)
        => RollImpl(ctx.Message, dices);

        
    internal static async Task RollImpl(DiscordMessage message, int maxValue = 6)
    {
        string? result = null;
        if (maxValue > 1)
            lock (rng) result = (rng.Next(maxValue) + 1).ToString();
        if (string.IsNullOrEmpty(result))
            await message.ReactWithAsync(DiscordEmoji.FromUnicode("💩"), $"How is {maxValue} a positive natural number?").ConfigureAwait(false);
        else
            await message.Channel.SendMessageAsync(result).ConfigureAwait(false);
    }
        
    internal static async Task RollImpl(DiscordMessage message, string dices)
    {
        var result = "";
        var embed = new DiscordEmbedBuilder();
        if (dices is string dice && DiceNotationPattern().Matches(dice) is {Count: > 0 and <= EmbedPager.MaxFields } matches)
        {
            var grandTotal = 0;
            foreach (Match m in matches)
            {
                result = "";
                if (!int.TryParse(m.Groups["num"].Value, out var num))
                    num = 1;
                if (int.TryParse(m.Groups["face"].Value, out var face)
                    && num is > 0 and < 101 
                    && face is > 1 and < 1001)
                {
                    List<int> rolls;
                    lock (rng) rolls = Enumerable.Range(0, num).Select(_ => rng.Next(face) + 1).ToList();
                    var total = rolls.Sum();
                    var totalStr = total.ToString();
                    if (int.TryParse(m.Groups["mod"].Value, out var mod) && mod > 0)
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
            await RollImpl(message).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(result) && embed == null)
            await message.ReactWithAsync(DiscordEmoji.FromUnicode("💩"), "Invalid dice description passed").ConfigureAwait(false);
        else if (embed != null)
            await message.Channel.SendMessageAsync(new DiscordMessageBuilder().WithEmbed(embed).WithReply(message.Id)).ConfigureAwait(false);
        else
            await message.Channel.SendMessageAsync(new DiscordMessageBuilder().WithContent(result).WithReply(message.Id)).ConfigureAwait(false);
    }

    [Command("random"), Aliases("rng"), Hidden, Cooldown(1, 3, CooldownBucketType.Channel)]
    [Description("Provides random stuff")]
    public async Task RandomShit(CommandContext ctx, string stuff)
    {
        stuff = stuff.ToLowerInvariant();
        switch (stuff)
        {
            case "game":
            case "serial":
            case "productcode":
            case "product code":
            {
                var db = new ThumbnailDb();
                await using var _ = db.ConfigureAwait(false);
                var count = await db.Thumbnail.CountAsync().ConfigureAwait(false);
                if (count == 0)
                {
                    await ctx.Channel.SendMessageAsync("Sorry, I have no information about a single game yet").ConfigureAwait(false);
                    return;
                }

                var tmpRng = new Random().Next(count);
                var productCode = await db.Thumbnail.Skip(tmpRng).Take(1).FirstOrDefaultAsync().ConfigureAwait(false);
                if (productCode == null)
                {
                    await ctx.Channel.SendMessageAsync("Sorry, there's something with my brains today. Try again or something").ConfigureAwait(false);
                    return;
                }

                await ProductCodeLookup.LookupAndPostProductCodeEmbedAsync(ctx.Client, ctx.Message, ctx.Channel, [productCode.ProductCode]).ConfigureAwait(false);
                break;
            }
            default:
                await Roll(ctx, comment: stuff).ConfigureAwait(false);
                break;
        }
    }

    [Command("8ball"), Cooldown(20, 60, CooldownBucketType.Channel)]
    [Description("Provides a ~~random~~ objectively best answer to your question")]
    public async Task EightBall(CommandContext ctx, [RemainingText, Description("A yes/no question")] string question)
    {
        question = question.ToLowerInvariant();
        if (question.StartsWith("when "))
            await When(ctx, question[5..]).ConfigureAwait(false);
        else
        {
            string answer;
            var pool = string.IsNullOrEmpty(question) ? EightBallSnarkyComments : EightBallAnswers;
            lock (rng) answer = pool[rng.Next(pool.Count)];
            if (answer.StartsWith(':') && answer.EndsWith(':'))
                answer = ctx.Client.GetEmoji(answer, "🔮");
            await ctx.RespondAsync(answer).ConfigureAwait(false);
        }
    }

    [Command("when"), Hidden, Cooldown(20, 60, CooldownBucketType.Channel)]
    [Description("Provides advanced clairvoyance services to predict the time frame for specified event with maximum accuracy")]
    public async Task When(CommandContext ctx, [RemainingText, Description("Something to happen")] string something = "")
    {
        var question = something.Trim().TrimEnd('?').ToLowerInvariant().StripInvisibleAndDiacritics().ToCanonicalForm();
        var prefix = DateTime.UtcNow.ToString("yyyyMMddHH");
        var crng = new Random((prefix + question).GetHashCode());
        var number = crng.Next(100) + 1;
        var unit = EightBallTimeUnits[crng.Next(EightBallTimeUnits.Count)];
        if (number > 1)
        {
            if (unit.EndsWith("ry"))
                unit = unit[..^1] + "ie";
            unit += "s";
            if (unit == "millenniums")
                unit = "millennia";
        }
        var willWont = crng.NextDouble() < 0.5 ? "will" : "won't";
        await ctx.RespondAsync($"🔮 My psychic powers tell me it {willWont} happen in the next **{number} {unit}** 🔮").ConfigureAwait(false);
    }

    [Group("how"), Hidden, Cooldown(20, 60, CooldownBucketType.Channel)]
    [Description("Provides advanced clairvoyance services to predict the exact amount of anything that could be measured")]
    public class How: BaseCommandModuleCustom
    {
        [Command("much"), Aliases("many")]
        [Description("Provides advanced clairvoyance services to predict the exact amount of anything that could be measured")]
        public async Task Much(CommandContext ctx, [RemainingText, Description("much or many ")] string ofWhat = "")
        {
            var question = ofWhat.Trim().TrimEnd('?').ToLowerInvariant().StripInvisibleAndDiacritics().ToCanonicalForm();
            var prefix = DateTime.UtcNow.ToString("yyyyMMddHH");
            var crng = new Random((prefix + question).GetHashCode());
            if (crng.NextDouble() < 0.0001)
                await ctx.RespondAsync($"🔮 My psychic powers tell me the answer should be **3.50** 🔮").ConfigureAwait(false);
            else
                await ctx.RespondAsync($"🔮 My psychic powers tell me the answer should be **{crng.Next(100) + 1}** 🔮").ConfigureAwait(false);
        }
    }

    [Command("rate"), Cooldown(20, 60, CooldownBucketType.Channel)]
    [Description("Gives a ~~random~~ expert judgment on the matter at hand")]
    public async Task Rate(CommandContext ctx, [RemainingText, Description("Something to rate")] string whatever = "")
    {
        try
        {
            var funMult = DateTime.UtcNow is {Month: 4, Day: 1} ? 100 : Config.FunMultiplier;
            var choices = RateAnswers;
            var choiceFlags = new HashSet<char>();
            whatever = whatever.ToLowerInvariant().StripInvisibleAndDiacritics();
            var originalWhatever = whatever;
            var matches = Instead().Matches(whatever);
            if (matches.Any())
            {
                var insteadWhatever = matches.Last().Groups["instead"].Value.TrimEager();
                if (!string.IsNullOrEmpty(insteadWhatever))
                    whatever = insteadWhatever;
            }
            foreach (var attachment in ctx.Message.Attachments)
                whatever += $" {attachment.FileSize}";

            var nekoUser = await ctx.Client.GetUserAsync(272032356922032139ul).ConfigureAwait(false);
            var nekoMember = await ctx.Client.GetMemberAsync(nekoUser).ConfigureAwait(false);
            var nekoMatch = new HashSet<string>(new[] {nekoUser.Id.ToString(), nekoUser.Username, nekoMember?.DisplayName ?? "neko", "neko", "nekotekina",});
            var kdUser = await ctx.Client.GetUserAsync(272631898877198337ul).ConfigureAwait(false);
            var kdMember = await ctx.Client.GetMemberAsync(kdUser).ConfigureAwait(false);
            var kdMatch = new HashSet<string>(new[] {kdUser.Id.ToString(), kdUser.Username, kdMember?.DisplayName ?? "kd-11", "kd", "kd-11", "kd11", });
            var botUser = ctx.Client.CurrentUser;
            var botMember = await ctx.Client.GetMemberAsync(botUser).ConfigureAwait(false);
            var botMatch = new HashSet<string>(new[] {botUser.Id.ToString(), botUser.Username, botMember?.DisplayName ?? "RPCS3 bot", "yourself", "urself", "yoself",});

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
                    suffix = word[..tmp.Length];
                    word = tmp;
                }
                tmp = word.TrimStart(Prefixes);
                if (tmp.Length != word.Length)
                {
                    result.Append(word[..^tmp.Length]);
                    word = tmp;
                }
                if (word.EndsWith("'s"))
                {
                    suffix = "'s" + suffix;
                    word = word[..^2];
                }

                void MakeCustomRoleRating(DiscordMember? mem)
                {
                    if (mem is null || choiceFlags.Contains('f'))
                        return;
                        
                    var roleList = mem.Roles.ToList();
                    if (roleList.Count == 0)
                        return;
                        
                    var role = roleList[new Random((prefix + mem.Id).GetHashCode()).Next(roleList.Count)].Name?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(role))
                        return;
                        
                    if (role.EndsWith('s'))
                        role = role[..^1];
                    var article = Vowels.Contains(role[0]) ? "n" : "";
                    choices = RateAnswers.Concat(Enumerable.Repeat($"Pretty fly for a{article} {role} guy", RateAnswers.Count * funMult / 20)).ToList();
                    choiceFlags.Add('f');
                }

                var appended = false;
                DiscordMember? member = null;
                if (Me.Contains(word))
                {
                    member = ctx.Member;
                    word = ctx.Message.Author.Id.ToString();
                    result.Append(word);
                    appended = true;
                }
                else if (word == "my")
                {
                    result.Append(ctx.Message.Author.Id).Append("'s");
                    appended = true;
                }
                else if (botMatch.Contains(word))
                {
                    word = ctx.Client.CurrentUser.Id.ToString();
                    result.Append(word);
                    appended = true;
                }
                else if (Your.Contains(word))
                {
                    result.Append(ctx.Client.CurrentUser.Id).Append("'s");
                    appended = true;
                }
                else if (word.StartsWith("actually") || word.StartsWith("nevermind") || word.StartsWith("nvm"))
                {
                    result.Clear();
                    appended = true;
                }
                if (member is null && i == 0 && await ctx.ResolveMemberAsync(word).ConfigureAwait(false) is DiscordMember m)
                    member = m;
                if (member != null)
                {
                    if (suffix.Length == 0)
                        MakeCustomRoleRating(member);
                    if (!appended)
                    {
                        result.Append(member.Id);
                        appended = true;
                    }
                }
                if (nekoMatch.Contains(word))
                {
                    if (i == 0 && suffix.Length == 0)
                    {
                        choices = RateAnswers.Concat(Enumerable.Repeat("Ugh", RateAnswers.Count * 3 * funMult)).ToList();
                        MakeCustomRoleRating(nekoMember);
                    }
                    result.Append(nekoUser.Id);
                    appended = true;
                }
                if (kdMatch.Contains(word))
                {
                    if (i == 0 && suffix.Length == 0)
                    {
                        choices = RateAnswers.Concat(Enumerable.Repeat("RSX genius", RateAnswers.Count * 3 * funMult)).ToList();
                        MakeCustomRoleRating(kdMember);
                    }
                    result.Append(kdUser.Id);
                    appended = true;
                }
                if (!appended)
                    result.Append(word);
                result.Append(suffix).Append(' ');
            }
            whatever = result.ToString();
            var cutIdx = whatever.LastIndexOf("never mind", StringComparison.Ordinal);
            if (cutIdx > -1)
                whatever = whatever[cutIdx..];
            whatever = whatever.Replace("'s's", "'s").TrimStart(EveryTimable).Trim();
            if (whatever.StartsWith("rate "))
                whatever = whatever[("rate ".Length)..];
            if (originalWhatever == "sonic" || originalWhatever.Contains("sonic the"))
                choices = RateAnswers
                    .Concat(Enumerable.Repeat("💩 out of 🦔", RateAnswers.Count * funMult))
                    .Concat(Enumerable.Repeat("Sonic out of 🦔", funMult))
                    .Concat(Enumerable.Repeat("Sonic out of 10", funMult))
                    .ToList();

            if (string.IsNullOrEmpty(whatever))
                await ctx.Channel.SendMessageAsync("Rating nothing makes _**so much**_ sense, right?").ConfigureAwait(false);
            else
            {
                var seed = (prefix + whatever).GetHashCode(StringComparison.CurrentCultureIgnoreCase);
                var seededRng = new Random(seed);
                var answer = choices[seededRng.Next(choices.Count)];
                var msgBuilder = new DiscordMessageBuilder()
                    .WithContent(answer)
                    .WithReply(ctx.Message.Id);
                await ctx.RespondAsync(msgBuilder).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, $"Failed to rate {whatever}");
        }
    }

    [Command("meme"), Aliases("memes"), Cooldown(1, 30, CooldownBucketType.Channel), Hidden]
    [Description("No, memes are not implemented yet")]
    public async Task Memes(CommandContext ctx, [RemainingText] string? _ = null)
    {
        var ch = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
        var msgBuilder = new DiscordMessageBuilder()
            .WithContent($"{ctx.User.Mention} congratulations, you're the meme");
        if (ch.Id == ctx.Channel.Id)
            msgBuilder.WithReply(ctx.Message.Id);
        await ch.SendMessageAsync(msgBuilder).ConfigureAwait(false);
    }

    [Command("firmware"), Aliases("fw"), Cooldown(1, 10, CooldownBucketType.Channel)]
    [Description("Checks for latest PS3 firmware version")]
    public Task Firmware(CommandContext ctx) => Psn.Check.GetFirmwareAsync(ctx);

    [Command("compare"), Hidden]
    [Description("Calculates the similarity metric of two phrases from 0 (completely different) to 1 (identical)")]
    public Task Compare(CommandContext ctx, string strA, string strB)
    {
        var result = strA.GetFuzzyCoefficientCached(strB);
        return ctx.Channel.SendMessageAsync($"Similarity score is {result:0.######}");
    }

    [Command("productcode"), Aliases("pci", "decode")]
    [Description("Describe Playstation product code")]
    public async Task ProductCode(CommandContext ctx, [RemainingText, Description("Product code such as BLUS12345 or SCES")] string productCode)
    {
        productCode = ProductCodeLookup.GetProductIds(productCode).FirstOrDefault() ?? productCode;
        productCode = productCode.ToUpperInvariant();
        if (productCode.Length > 3)
        {
            var dsc = ProductCodeDecoder.Decode(productCode);
            var info = string.Join('\n', dsc);
            if (productCode.Length == 9)
            {
                var embed = await ctx.Client.LookupGameInfoAsync(productCode).ConfigureAwait(false);
                embed.AddField("Product code info", info);
                await ctx.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
            }
            else
                await ctx.Channel.SendMessageAsync(info).ConfigureAwait(false);
        }
        else
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Invalid product code").ConfigureAwait(false);
    }
}