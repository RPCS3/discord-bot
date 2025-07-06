﻿using CompatApiClient.Utils;
using CompatBot.Database;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class EventNameAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        await using var db = await BotDb.OpenReadAsync().ConfigureAwait(false);
#if DEBUG        
        var currentTime = DateTime.UtcNow.AddYears(-10);
#else
        var currentTime = DateTime.UtcNow;
#endif
        var currentTicks = currentTime.Ticks;
        IEnumerable<string?> query;
        if (context.UserInput is not { Length: > 0 } input)
        {
            query = db.EventSchedule
                .Where(e => e.End >= currentTicks)
                .OrderBy(e => Math.Min(Math.Abs(currentTicks - e.Start), Math.Abs(currentTicks - e.End)))
                .AsEnumerable()
                .SelectMany(e => new[]{e.EventName, e.Name})
                .Where(n => n != null);
        }
        else
        {
            var prefixEvent = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.EventName != null && e.EventName.StartsWith(input))
                .OrderBy(e => e.Start)
                .Take(25);
            var subEvent = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.EventName != null && e.EventName.Contains(input))
                .OrderBy(e => e.Start)
                .Take(50);
            var fuzzyEvent = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.EventName != null)
                .OrderBy(e => e.Start)
                .AsNoTracking()
                .AsEnumerable()
                .Select(e => new { coef = e.EventName.GetFuzzyCoefficientCached(input), evt = e })
                .Where(i => i.coef > 0.5)
                .OrderByDescending(i => i.coef)
                .Take(25)
                .Select(i => i.evt.EventName);
            var eventNames = prefixEvent
                .Concat(subEvent)
                .Select(e => e.EventName!)
                .Distinct()
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Concat(fuzzyEvent)
                .Distinct()
                .Take(10);
            
            var prefix = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.Name != null && e.Name.StartsWith(input))
                .OrderBy(e => e.Start)
                .Take(25);
            var sub = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.Name != null && e.Name.Contains(input))
                .OrderBy(e => e.Start)
                .Take(50);
            var fuzzy = db.EventSchedule
                .Where(e => e.End >= currentTicks && e.Name != null)
                .OrderBy(e => e.Start)
                .AsNoTracking()
                .AsEnumerable()
                .Select(e => new { coef = e.Name.GetFuzzyCoefficientCached(input), evt = e })
                .Where(i => i.coef > 0.5)
                .OrderByDescending(i => i.coef)
                .Take(25)
                .Select(i => i.evt.Name);
            var names = prefix
                .Concat(sub)
                .Select(e => e.Name!)
                .Distinct()
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Concat(fuzzy)
                .Distinct();
            query = new[] { context.UserInput }
                .Concat(eventNames)
                .Concat(names);
        }
        return query
            .Distinct()
            .Take(25)
            .Select(n => new DiscordAutoCompleteChoice(n.Trim(100), n))
            .ToList();
    }
}