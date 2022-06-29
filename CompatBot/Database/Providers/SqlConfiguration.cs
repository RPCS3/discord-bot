using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database.Providers;

internal static class SqlConfiguration
{
    internal const string ConfigVarPrefix = "ENV-";

    public static async Task RestoreAsync()
    {
        await using var db = new BotDb();
        var setVars = await db.BotState.AsNoTracking().Where(v => v.Key.StartsWith(ConfigVarPrefix)).ToListAsync().ConfigureAwait(false);
        if (setVars.Any())
        {
            foreach (var stateVar in setVars)
                if (stateVar.Value is string value)
                    Config.InMemorySettings[stateVar.Key[ConfigVarPrefix.Length ..]] = value;
            Config.RebuildConfiguration();
        }
    }
}