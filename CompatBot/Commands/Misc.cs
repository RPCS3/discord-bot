using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using org.mariuszgromada.math.mxparser;

namespace CompatBot.Commands
{
    internal sealed class Misc: BaseCommandModule
    {
        private readonly Random rng = new Random();

        private static readonly List<string> EightBallAnswers = new List<string> {
            "Nah mate", "Ya fo sho", "Fo shizzle mah nizzle", "Yuuuup", "Nope", "Njet", "Da", "Maybe", "I don't know",
            "I don't care", "Affirmative", "Sure", "Yeah, why not", "Most likely", "Sim", "Oui", "Heck yeah!", "Roger that",
            "Aye!", "Yes without a doubt m8!", "Who cares", "Maybe yes, maybe not", "Maybe not, maybe yes", "Ugh",
            "Probably", "Ask again later", "Error 404: answer not found", "Don't ask me that again",
            "You should think twice before asking", "You what now?", "Bloody hell, answering that ain't so easy",
            "Of course not", "Seriously no", "Noooooooooo", "Most likely not", "Não", "Non", "Hell no", "Absolutely not",
            "Ask Neko", "Ask Ani", "I'm pretty sure that's illegal!", "<:cell_ok_hand:324618647857397760>",
            "Don't be an idiot. YES.", "What do *you* think?", "Only on Wednesdays", "Look in the mirror, you know the answer already"
        };

        private static readonly List<string> RateAnswers = new List<string>
        {
            "Bad", "Very bad", "Pretty bad", "Horrible", "Ugly", "Disgusting", "Literally the worst",
            "Not interesting", "Simply ugh", "I don't like it! You shouldn't either!", "Just like you, 💩",
            "Not approved", "Big Mistake", "Ask MsLow", "The opposite of good",
            "Could be better", "Could be worse", "Not so bad",
            "I likesss!", "Pretty good", "Guchi gud", "Amazing!", "Glorious!", "Very good", "Excellent...",
            "Magnificent", "Rate bot says he likes, so you like too",
            "If you reorganize the words it says \"pretty cool\"", "I approve",
            "I need more time to think about it", "It's ok, nothing and no one is perfect",
            "<:morgana_sparkle:315899996274688001>　やるじゃねーか！", "Not half bad 👍", "🆗", "😐", "🤮", "Belissimo!",
            "So-so"
        };

        [Command("credits"), Aliases("about")]
        [Description("Author Credit")]
        public async Task Credits(CommandContext ctx)
        {
            var embed = new DiscordEmbedBuilder
            {
                Title = "RPCS3 Compatibility Bot",
                Url = "https://github.com/RPCS3/discord-bot",
                Description = "Made by:\n" +
                              "\tRoberto Anić Banić aka nicba1010\n" +
                              "\t13xforever",
                Color = DiscordColor.Purple,
            };
            await ctx.RespondAsync(embed: embed.Build());
        }

        [Command("math")]
        [Description("Math, here you go Juhn")]
        public async Task Math(CommandContext ctx, [RemainingText, Description("Math expression")] string expression)
        {
            var typing = ctx.TriggerTypingAsync();
            var result = @"Something went wrong ¯\\_(ツ)\_/¯" + "\nMath is hard, yo";
            try
            {
                var expr = new Expression(expression);
                result = expr.calculate().ToString();
            }
            catch (Exception e)
            {
                ctx.Client.DebugLogger.LogMessage(LogLevel.Warning, "", "Math failed: " + e.Message, DateTime.Now);
            }
            await ctx.RespondAsync(result).ConfigureAwait(false);
            await typing.ConfigureAwait(false);
        }

        [Command("roll")]
        [Description("Generates a random number between 1 and N (default 10). Can also roll dices like `2d6`")]
        public async Task Roll(CommandContext ctx, [Description("Some positive number or a dice")] string something)
        {
            var result = "";
            switch (something)
            {
                case string val when int.TryParse(val, out var maxValue) && maxValue > 1:
                    lock (rng) result = (rng.Next(maxValue) + 1).ToString();
                    break;
                case string dice when Regex.IsMatch(dice, @"\d+d\d+"):
                    var typingTask = ctx.TriggerTypingAsync();
                    var diceParts = dice.Split('d', StringSplitOptions.RemoveEmptyEntries);
                    if (int.TryParse(diceParts[0], out var num) && int.TryParse(diceParts[1], out var face) &&
                        0 < num && num < 101 &&
                        1 < face && face < 1001)
                    {
                        List<int> rolls;
                        lock (rng) rolls = Enumerable.Range(0, num).Select(_ => rng.Next(face) + 1).ToList();
                        result = "Total: " + rolls.Sum();
                        if (rolls.Count > 1)
                            result += "\nRolls: " + string.Join(' ', rolls);
                    }
                    await typingTask.ConfigureAwait(false);
                    break;
            }
            if (string.IsNullOrEmpty(result))
                await ctx.Message.CreateReactionAsync(DiscordEmoji.FromUnicode("💩")).ConfigureAwait(false);
            else
                await ctx.RespondAsync(result).ConfigureAwait(false);
        }

        [Command("8ball")]
        [Description("Generates a ~~random~~ perfect answer to your question")]
        public async Task EightBall(CommandContext ctx, [RemainingText, Description("A yes/no question")] string question)
        {
            string answer;
            lock (rng) answer = EightBallAnswers[rng.Next(EightBallAnswers.Count)];
            await ctx.RespondAsync(answer).ConfigureAwait(false);
        }

        [Command("rate")]
        [Description("Gives an ~~unrelated~~ expert judgement on the matter at hand")]
        public async Task Rate(CommandContext ctx, [RemainingText, Description("Something to rate")] string whatever)
        {
            string answer;
            lock (rng) answer = RateAnswers[rng.Next(RateAnswers.Count)];
            await ctx.RespondAsync(answer).ConfigureAwait(false);
        }
    }
}
