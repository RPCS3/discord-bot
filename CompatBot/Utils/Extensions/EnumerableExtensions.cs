﻿namespace CompatBot.Utils;

public static class EnumerableExtensions
{
    public static IEnumerable<TResult> Pairwise<T, TResult>(this IEnumerable<T> source, Func<T, T, TResult> selector)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        if (selector == null)
            throw new ArgumentNullException(nameof(selector));

        using var e = source.GetEnumerator();
        if (!e.MoveNext())
            yield break;

        T prev = e.Current;
        if (!e.MoveNext())
            yield break;

        do
        {
            yield return selector(prev, e.Current);
            prev = e.Current;
        } while (e.MoveNext());
    }

    public static IEnumerable<T> Single<T>(T item)
    {
        yield return item;
    }

    public static T? RandomElement<T>(this IList<T> collection, int? seed = null)
    {
        if (collection.Count > 0)
        {
            var rng = seed.HasValue ? new(seed.Value) : new Random();
            return collection[rng.Next(collection.Count)];
        }
        return default;
    }

    public static bool AnyPatchesApplied(this Dictionary<string, int> patches)
        => patches.Values.Any(v => v > 0);
        
    public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> items, int maxItems)
    {
        return items.Select((item, inx) => new { item, inx })
            .GroupBy(x => x.inx / maxItems)
            .Select(g => g.Select(x => x.item));
    }

    public static List<T> ToList<T>(this IAsyncEnumerable<T> items)
        => items.ToBlockingEnumerable().ToList();
}