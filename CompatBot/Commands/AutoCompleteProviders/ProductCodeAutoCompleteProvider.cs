using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands.AutoCompleteProviders;

public class ProductCodeAutoCompleteProvider: IAutoCompleteProvider
{
    public async ValueTask<IEnumerable<DiscordAutoCompleteChoice>> AutoCompleteAsync(AutoCompleteContext context)
    {
        await using var db = new ThumbnailDb();
        IEnumerable<(string code, string title)> result;
        if (context.UserInput is not { Length: > 0 } prefix)
        {
            var popularNames = StatsStorage
                .GetGameStats()
                .Select(s => s.name)
                .Take(25)
                .ToHashSet();
            var popular = db.Thumbnail
                .Where(
                    t => t.CompatibilityStatus != null
                         && t.Name != null
                         && popularNames.Contains(t.Name)
                ).AsNoTracking()
                .AsEnumerable()
                .Select(t => (code: t.ProductCode, title: t.Name!))
                .DistinctBy(i => i.title)
                .Take(25);
            var itemCount = await db.Thumbnail.CountAsync().ConfigureAwait(false);
            var rng = new Random();
            var randomIds = Enumerable.Range(1, 50).Select(_ => rng.Next(itemCount)).ToHashSet();
            var random = db.Thumbnail
                .Where(
                    t => t.CompatibilityStatus != null
                    && t.Name != null
                    && randomIds.Contains(t.Id)
                )
                .AsNoTracking()
                .AsEnumerable()
                .Select(t => (code: t.ProductCode, title: t.Name!))
                .DistinctBy(i => i.title)
                .Take(50);
            result = popular
                .Concat(random)
                .Distinct()
                .Take(25);
        }
        else
        {
            prefix = prefix.ToLowerInvariant();
            var prefixMatches = db.Thumbnail
                .Where(t => t.ProductCode.StartsWith(prefix) || (t.Name != null && t.Name.StartsWith(prefix)))
                .OrderBy(t => t.Name)
                .ThenBy(t => t.ProductCode)
                .Take(25)
                .AsNoTracking()
                .AsEnumerable()
                .Select(t => (code: t.ProductCode, title: t.Name!));
            var substringMatches= db.Thumbnail
                .Where(t => t.ProductCode.Contains(prefix) || (t.Name != null && t.Name.Contains(prefix)))
                .OrderBy(t => t.Name)
                .ThenBy(t => t.ProductCode)
                .Take(50)
                .AsNoTracking()
                .AsEnumerable()
                .Select(t => (code: t.ProductCode, title: t.Name!));
            result = prefixMatches
                .Concat(substringMatches)
                .Distinct()
                .Take(25);
        }
        return result.Select(i => new DiscordAutoCompleteChoice($"{i.code}: {i.title}", i.code)).ToList();
    }
}