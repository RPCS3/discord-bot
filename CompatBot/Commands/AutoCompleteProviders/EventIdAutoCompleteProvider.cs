using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class EventIdAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        await using var db = BotDb.OpenRead();
        IEnumerable<EventSchedule> query;
        if (context.UserInput is not { Length: > 0 } input)
        {
            query = db.EventSchedule
                .OrderByDescending(e => e.Id)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable();
        }
        else
        {
            var prefix = db.EventSchedule
                .Where(e => e.Id.ToString().StartsWith(input) || e.Name != null && e.Name.StartsWith(input))
                .OrderBy(e => e.Start)
                .Take(25);
            var sub = db.EventSchedule
                .Where(e => e.Id.ToString().Contains(input) || e.Name != null && e.Name.Contains(input))
                .OrderBy(e => e.Start)
                .Take(50);
            var currentTicks = DateTime.UtcNow.Ticks;
            var fuzzy = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.Name != null)
                .OrderBy(e => e.Start)
                .AsNoTracking()
                .AsEnumerable()
                .Select(e => new { coef = e.Name.GetFuzzyCoefficientCached(input), evt = e })
                .Where(i => i.coef > 0.5)
                .OrderByDescending(i => i.coef)
                .Take(25)
                .Select(i => i.evt);
            query = prefix
                .Concat(sub)
                .Distinct()
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Concat(fuzzy)
                .Distinct();
        }
        return query
            .Distinct()
            .Take(25)
            .Select(n => new DiscordAutoCompleteChoice($"{n.Id}: {n.Name}", n.Id))
            .ToList();
    }
}