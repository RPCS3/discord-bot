using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CompatBot.Database.Providers
{
    internal static class ScrapeStateProvider
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(15);

        public static bool IsFresh(long timestamp)
        {
            return IsFresh(new DateTime(timestamp, DateTimeKind.Utc));
        }

        public static bool IsFresh(DateTime timestamp)
        {
            return timestamp.Add(CheckInterval) > DateTime.UtcNow;
        }

        public static bool IsFresh(string locale, string containerId = null)
        {
            var id = GetId(locale, containerId);
            using (var db = new ThumbnailDb())
            {
                var timestamp = string.IsNullOrEmpty(id) ? db.State.OrderBy(s => s.Timestamp).FirstOrDefault() : db.State.FirstOrDefault(s => s.Locale == id);
                if (timestamp?.Timestamp is long checkDate && checkDate > 0)
                    return IsFresh(new DateTime(checkDate, DateTimeKind.Utc));
            }
            return false;
        }


        public static bool IsFresh(string locale, DateTime dataTimestamp)
        {
            using (var db = new ThumbnailDb())
            {
                var timestamp = string.IsNullOrEmpty(locale) ? db.State.OrderBy(s => s.Timestamp).FirstOrDefault() : db.State.FirstOrDefault(s => s.Locale == locale);
                if (timestamp?.Timestamp is long checkDate && checkDate > 0)
                    return new DateTime(checkDate, DateTimeKind.Utc) > dataTimestamp;
            }
            return false;
        }


        public static async Task SetLastRunTimestampAsync(string locale, string containerId = null)
        {
            if (string.IsNullOrEmpty(locale))
                throw new ArgumentException(nameof(locale));

            var id = GetId(locale, containerId);
            using (var db = new ThumbnailDb())
            {
                var timestamp = db.State.FirstOrDefault(s => s.Locale == id);
                if (timestamp == null)
                    db.State.Add(new State {Locale = id, Timestamp = DateTime.UtcNow.Ticks});
                else
                    timestamp.Timestamp = DateTime.UtcNow.Ticks;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public static async Task CleanAsync(CancellationToken cancellationToken)
        {
            using (var db = new ThumbnailDb())
            {
                var latestTimestamp = db.State.OrderByDescending(s => s.Timestamp).FirstOrDefault()?.Timestamp;
                if (!latestTimestamp.HasValue)
                    return;

                var cutOff = new DateTime(latestTimestamp.Value, DateTimeKind.Utc).Add(-CheckInterval);
                var oldItems = db.State.Where(s => s.Timestamp < cutOff.Ticks);
                db.State.RemoveRange(oldItems);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static string GetId(string locale, string containerId)
        {
            if (string.IsNullOrEmpty(locale) || string.IsNullOrEmpty(containerId))
                return locale;
            return $"{locale} - {containerId}";
        }
    }
}
