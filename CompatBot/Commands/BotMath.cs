using System;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using org.mariuszgromada.math.mxparser;

namespace CompatBot.Commands
{
    [Group("math")]
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

        [Command("help"), LimitedToSpamChannel, Cooldown(1, 5, CooldownBucketType.Channel)]
        [Description("General math expression help, or description of specific math word")]
        public Task Help(CommandContext ctx, string word = null)
        {
            return ctx.RespondAsync("Help for all the features and built-in constants and functions could be found at <https://mathparser.org/mxparser-math-collection/>");
        }
    }
}