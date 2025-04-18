using CompatBot.Database;

namespace CompatBot.Utils.Extensions;

internal static class ThumbnailDbExtensions
{
    internal static IQueryable<Thumbnail> WithStatus(this IQueryable<Thumbnail> queryBase, CompatStatus? status, bool exact)
        => (status, exact) switch
        {
            (not null, true) => queryBase.Where(g => g.CompatibilityStatus == status),
            (not null, false) => queryBase.Where(g => g.CompatibilityStatus >= status),
            (null, _) => queryBase
        };
}