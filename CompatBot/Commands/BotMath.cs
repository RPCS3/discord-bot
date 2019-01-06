using System;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using org.mariuszgromada.math.mxparser;

namespace CompatBot.Commands
{
    [Group("math"), TriggersTyping]
    [Description("Math, here you go Juhn. Use `math help` for syntax help")]
    internal sealed class BotMath : BaseCommandModuleCustom
    {
        [GroupCommand, Priority(9)]
        public async Task Expression(CommandContext ctx, [RemainingText, Description("Math expression")] string expression)
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

        [Command("help"), LimitedToSpamChannel, Cooldown(1, 5*60, CooldownBucketType.Global)]
        [Description("General math expression help, or description of specific math word")]
        public Task Help(CommandContext ctx, string word = null)
        {
            var help = string.IsNullOrEmpty(word) ? mXparser.getHelp() : mXparser.getHelp(word);
            var hasR = help.Contains('\r');
            var hasN = help.Contains('\n');
            if (Environment.NewLine == "\r\n")
            {
                if (hasR && !hasN)
                    help = help.Replace("\r", Environment.NewLine);
                else if (hasN && !hasR)
                    help = help.Replace("\n", Environment.NewLine);
            }
            else if (Environment.NewLine == "\r" || Environment.NewLine == "\n")
            {
                if (hasR && hasN)
                    help = help.Replace("\r\n", Environment.NewLine);
                else if (Environment.NewLine == "\r" && hasN)
                    help = help.Replace("\n", Environment.NewLine);
                else if (Environment.NewLine == "\n" && hasR)
                    help = help.Replace("\r", Environment.NewLine);
            }
            return ctx.SendAutosplitMessageAsync($"```{help}```");
        }
    }
}