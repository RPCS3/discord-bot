using System.Globalization;
using DSharpPlus.Commands.Processors.SlashCommands;
using org.mariuszgromada.math.mxparser;
using License = org.mariuszgromada.math.mxparser.License;

namespace CompatBot.Commands;

internal sealed class BotMath
{
    static BotMath()
    {
        License.iConfirmNonCommercialUse("RPCS3");
    }
    
    [Command("calculate"), DefaultGroupCommand]
    [Description("Math; there you go, Juhn")]
    public async ValueTask Calc(SlashCommandContext ctx, [RemainingText, Description("Math expression or `help` for syntax link")] string expression)
    {
        if (expression.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.RespondAsync("Help for all the features and built-in constants and functions could be found at [mXparser website](<https://mathparser.org/mxparser-math-collection/>)", true);
            return;
        }

        var result = """
            Something went wrong ¯\\\_(ツ)\_/¯
            Math is hard, yo
            """;
        try
        {
            mXparser.resetCancelCurrentCalculationFlag();
            var expr = new Expression(expression);
            const int timeout = 1_000;
            var cts = new CancellationTokenSource(timeout);
            // ReSharper disable once MethodSupportsCancellation
            var delayTask = Task.Delay(timeout);
            var calcTask = Task.Run(() => expr.calculate().ToString(CultureInfo.InvariantCulture), cts.Token);
            await Task.WhenAny(calcTask, delayTask).ConfigureAwait(false);
            if (calcTask.IsCompletedSuccessfully)
            {
                result = await calcTask;
            }
            else
            {
                mXparser.cancelCurrentCalculation();
                result = "Calculation took too much time and all operations were cancelled";
            }
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Math failed");
        }
        await ctx.RespondAsync(result, true).ConfigureAwait(false);
    }
}