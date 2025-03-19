﻿using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class ExplainAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        await using var db = new BotDb();
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
                .Select(term => new{coeff=term.GetFuzzyCoefficientCached(prefix), term=term})
                .OrderByDescending(pair => pair.coeff)
                .Take(25)
                .Select(pair => pair.term);
            result = prefixMatches
                .Concat(substringMatches)
                .Concat(fuzzyMatches)
                .Distinct()
                .Take(25);
        }
        return result.Select(term => new DiscordAutoCompleteChoice(term, term)).ToList();
    }
}