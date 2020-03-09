using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers
{
    internal static class SqlConfiguration
    {
        internal const string ConfigVarPrefix = "ENV-";

        public static async Task RestoreAsync()
        {
            using var db = new BotDb();
            var setVars = await db.BotState.AsNoTracking().Where(v => v.Key.StartsWith(ConfigVarPrefix)).ToListAsync().ConfigureAwait(false);
            foreach (var v in setVars)
                Config.inMemorySettings[v.Key[(ConfigVarPrefix.Length)..]] = v.Value;
        }
    }
}
