using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.Database.Providers;

internal static class ScrapeStateProvider
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(365);

    public static bool IsFresh(long timestamp)
        => IsFresh(new DateTime(timestamp, DateTimeKind.Utc));

    public static bool IsFresh(DateTime timestamp)
        => timestamp.Add(CheckInterval) > DateTime.UtcNow;

    public static bool IsFresh(string locale, string? containerId = null)
    {
        var id = GetId(locale, containerId);
        using var db = new ThumbnailDb();
        var timestamp = string.IsNullOrEmpty(id) ? db.State.OrderBy(s => s.Timestamp).FirstOrDefault() : db.State.FirstOrDefault(s => s.Locale == id);
        if (timestamp?.Timestamp is long checkDate && checkDate > 0)
            return IsFresh(new DateTime(checkDate, DateTimeKind.Utc));
        return false;
    }

    public static bool IsFresh(string locale, DateTime dataTimestamp)
    {
        using var db = new ThumbnailDb();
        var timestamp = string.IsNullOrEmpty(locale) ? db.State.OrderBy(s => s.Timestamp).FirstOrDefault() : db.State.FirstOrDefault(s => s.Locale == locale);
        if (timestamp?.Timestamp is long checkDate && checkDate > 0)
            return new DateTime(checkDate, DateTimeKind.Utc) > dataTimestamp;
        return false;
    }

    public static async Task SetLastRunTimestampAsync(string locale, string? containerId = null)
    {
        if (string.IsNullOrEmpty(locale))
            throw new ArgumentException("Locale is mandatory", nameof(locale));

        var id = GetId(locale, containerId);
        await using var db = new ThumbnailDb();
        var timestamp = db.State.FirstOrDefault(s => s.Locale == id);
        if (timestamp == null)
            await db.State.AddAsync(new State {Locale = id, Timestamp = DateTime.UtcNow.Ticks}).ConfigureAwait(false);
        else
            timestamp.Timestamp = DateTime.UtcNow.Ticks;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    public static async Task CleanAsync(CancellationToken cancellationToken)
    {
        await using var db = new ThumbnailDb();
        var latestTimestamp = db.State.OrderByDescending(s => s.Timestamp).FirstOrDefault()?.Timestamp;
        if (!latestTimestamp.HasValue)
            return;

        var cutOff = new DateTime(latestTimestamp.Value, DateTimeKind.Utc).Add(-CheckInterval);
        var oldItems = db.State.Where(s => s.Timestamp < cutOff.Ticks);
        db.State.RemoveRange(oldItems);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetId(string locale, string? containerId)
        => string.IsNullOrEmpty(containerId) ? locale : $"{locale} - {containerId}";
}