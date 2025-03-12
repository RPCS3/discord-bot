using System.Globalization;
using org.mariuszgromada.math.mxparser;
using License = org.mariuszgromada.math.mxparser.License;

namespace CompatBot.Commands;

[Command("math")]
[Description("Math, here you go Juhn. Use `math help` for syntax help")]
internal sealed class BotMath
{
    static BotMath()
    {
        License.iConfirmNonCommercialUse("RPCS3");
    }
    
    [Command("calculate"), TextAlias("calc"), DefaultGroupCommand]
    [Description("12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901")]
    public async ValueTask Calc(CommandContext ctx, [RemainingText, Description("Math expression")] string expression)
    {
        if (string.IsNullOrEmpty(expression))
        {
            await Help(ctx).ConfigureAwait(false);
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
        await ctx.Channel.SendMessageAsync(result).ConfigureAwait(false);
    }

    [Command("help"), LimitedToSpamChannel]
    //[Cooldown(1, 5, CooldownBucketType.Channel)]
    [Description("General math expression help, or description of specific math word")]
    public Task Help(CommandContext ctx)
        => ctx.Channel.SendMessageAsync("Help for all the features and built-in constants and functions could be found at [mXparser website](<https://mathparser.org/mxparser-math-collection/>)");
}