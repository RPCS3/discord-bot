using CompatBot.Database;
using CompatBot.Utils.Extensions;
using TResult = System.Collections.Generic.IEnumerable<DSharpPlus.Entities.DiscordAutoCompleteChoice>;

namespace CompatBot.Commands.AutoCompleteProviders;

public class FilterActionAutoCompleteProvider: IAutoCompleteProvider
{
    static FilterActionAutoCompleteProvider()
    {
        var validValues = FilterActionExtensions.ActionFlagValues;
        minValue = (int)validValues[0];
        maxValue = (int)validValues.Aggregate((a, b) => a | b);
        choiceList = new DiscordAutoCompleteChoice[maxValue+1];
        choiceList[0] = new("Default", 0);
        choiceList[maxValue] = new("All", maxValue);
        for (var i = minValue; i < maxValue; i++)
            choiceList[i] = new($"{((FilterAction)i).ToFlagsString()}: {((FilterAction)i).ToString()}", i);
    }

    private static readonly int minValue;
    private static readonly int maxValue;
    private static readonly DiscordAutoCompleteChoice[] choiceList;
    private static readonly char[] Delimiters = { ',', ' ' };
    
    public ValueTask<TResult> AutoCompleteAsync(AutoCompleteContext context)
        => ValueTask.FromResult(GetChoices(Parse(context.UserInput)));

    private static int Parse(string? input)
    {
        if (input is not {Length: >0 and <7})
            return 0;
        return (int)input.ToFilterAction();
    }

    private static TResult GetChoices(int start)
    {
        List<DiscordAutoCompleteChoice> result = [choiceList[start]];
        for(var i=minValue; i<=maxValue; i <<= 1)
        {
            var nextVal = start | i;
            if (nextVal != start)
                result.Add(choiceList[nextVal]);
        }
        return result.Take(25);
    }
}