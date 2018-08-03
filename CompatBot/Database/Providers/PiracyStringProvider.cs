using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NReco.Text;

namespace CompatBot.Database.Providers
{
    internal static class PiracyStringProvider
    {
        private static readonly BotDb db = new BotDb();
        private static readonly object SyncObj = new object();
        private static readonly List<string> PiracyStrings;
        private static AhoCorasickDoubleArrayTrie<string> matcher;

        static PiracyStringProvider()
        {
            PiracyStrings = db.Piracystring.Select(ps => ps.String).ToList();
            RebuildMatcher();
        }

        public static async Task<bool> AddAsync(string trigger)
        {
            if (PiracyStrings.Contains(trigger, StringComparer.InvariantCultureIgnoreCase))
                return false;

            lock (SyncObj)
            {
                if (PiracyStrings.Contains(trigger, StringComparer.InvariantCultureIgnoreCase))
                    return false;

                PiracyStrings.Add(trigger);
                RebuildMatcher();
            }
            await db.Piracystring.AddAsync(new Piracystring {String = trigger}).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }

        public static async Task<bool> RemoveAsync(int id)
        {
            var dbItem = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id).ConfigureAwait(false);
            if (dbItem == null)
                return false;

            db.Piracystring.Remove(dbItem);
            if (!PiracyStrings.Contains(dbItem.String))
                return false;

            lock (SyncObj)
            {
                if (!PiracyStrings.Remove(dbItem.String))
                    return false;

                RebuildMatcher();
            }

            await db.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }

        public static Task<string> FindTriggerAsync(string str)
        {
            string result = null;
            matcher?.ParseText(str, h =>
                                   {
                                       result = h.Value;
                                       return false;
                                   });
            return Task.FromResult(result);
        }

        private static void RebuildMatcher()
        {
            matcher = PiracyStrings.Count == 0 ? null : new AhoCorasickDoubleArrayTrie<string>(PiracyStrings.ToDictionary(s => s, s => s), true);
        }
    }
}