using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class ExplainAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
        db.WithNoCase();
        IEnumerable<string> result;
        if (context.UserInput is not { Length: > 0 } prefix)
        {
            var allTerms = await db.Explanation
                .Select(e => e.Keyword)
                .AsNoTracking()
                .ToListAsync();
            var popular = StatsStorage
                .GetExplainStats()
                .Select(s => s.name)
                .Intersect(allTerms)
                .Take(25);
            var random = allTerms
                .OrderBy(n => n)
                .Take(50);
            result = popular
                .Concat(random)
                .Distinct()
                .Take(25);
        }
        else
        {
            prefix = prefix.ToLowerInvariant();
            var prefixMatches = db.Explanation
                .Where(e => e.Keyword.StartsWith(prefix))
                .OrderBy(e => e.Keyword)
                .Take(25)
                .Select(e => e.Keyword)
                .AsNoTracking()
                .AsEnumerable();
            var substringMatches= db.Explanation
                .Where(e => e.Keyword.Contains(prefix))
                .OrderBy(e => e.Keyword)
                .Take(50)
                .Select(e => e.Keyword)
                .AsNoTracking()
                .AsEnumerable();
            var fuzzyMatches = db.Explanation
                .Select(e => e.Keyword)
                .AsNoTracking()
                .AsEnumerable()
                .Select(term => new{coef=term.GetFuzzyCoefficientCached(prefix), term=term})
                .Where(i => i.coef > 0.5)
                .OrderByDescending(i => i.coef)
                .Take(25)
                .Select(i => i.term);
            result = prefixMatches
                .Concat(substringMatches)
                .Concat(fuzzyMatches)
                .Distinct()
                .Take(25);
        }
        return result.Select(
            term => new DiscordAutoCompleteChoice(
                $"{term} - {db.Explanation.AsNoTracking().First(i => i.Keyword == term).Text}".Trim(100),
                term
            )
        ).ToList();
    }
}