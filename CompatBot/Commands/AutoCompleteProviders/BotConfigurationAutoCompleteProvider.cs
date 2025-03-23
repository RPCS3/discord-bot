using System.Reflection;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class BotConfigurationAutoCompleteProvider: IAutoCompleteProvider
{
    private static readonly List<string> KnownConfigVariables = typeof(Config)
        .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.ExactBinding)
        .Select(pi => pi.Name)
        .OrderBy(n => n)
        .ToList(); 
    
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        if (!ModProvider.IsSudoer(context.User.Id))
            return [new($"{Config.Reactions.Denied} You are not authorized to use this command.", -1)];

        await using var db = new BotDb();
        IEnumerable<string> result;
        var input = context.UserInput;
        if (input is not { Length: > 0 })
        {
            var set = db.BotState
                .AsNoTracking()
                .Where(v => v.Key.StartsWith(SqlConfiguration.ConfigVarPrefix))
                .OrderBy(v => v.Key)
                .Take(25)
                .Select(v => v.Key)
                .AsEnumerable()
                .Select(k => k[SqlConfiguration.ConfigVarPrefix.Length ..]);
            result = set.Concat(KnownConfigVariables);
        }
        else
        {
            var prefix = KnownConfigVariables
                .Where(n => n.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                .Take(25);
            var sub = KnownConfigVariables
                .Where(n => n.Contains(input, StringComparison.OrdinalIgnoreCase))
                .Take(50);
            var fuzzy = KnownConfigVariables
                .Select(n => new { coef = n.GetFuzzyCoefficientCached(input), val = n })
                .Where(i => i.coef > 0.5)
                .OrderByDescending(i => i.coef)
                .Take(25)
                .Select(i => i.val);
            result = prefix
                .Concat(sub)
                .Concat(fuzzy);
        }
        return result
            .Distinct()
            .Take(25)
            .Select(n => new DiscordAutoCompleteChoice(n, n)).ToList();
    }
}