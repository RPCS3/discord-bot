using CompatBot.Database;

namespace CompatBot.Utils.Extensions;

internal static class ThumbnailDbExtensions
{
    internal static IQueryable<Thumbnail> WithStatus(this IQueryable<Thumbnail> queryBase, CompatStatus status, bool exact)
        => exact
            ? queryBase.Where(g => g.CompatibilityStatus == status)
            : queryBase.Where(g => g.CompatibilityStatus >= status);
}