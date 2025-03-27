using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

internal static partial class Bot
{
    [Command("config"), RequiresBotSudoerRole]
    [Description("Commands to set or clear bot configuration variables")]
    internal static class Configuration
    {
        [Command("list"), TextAlias("show")]
        [Description("List set variable names")]
        public static async ValueTask List(SlashCommandContext ctx)
        {
            await using var db = BotDb.OpenRead();
            var setVars = await db.BotState
                .AsNoTracking()
                .Where(v => v.Key.StartsWith(SqlConfiguration.ConfigVarPrefix))
                .ToListAsync()
                .ConfigureAwait(false);
            if (setVars.Count > 0)
            {
                var result = new StringBuilder("Set variables:").AppendLine();
                foreach (var v in setVars)
                {
#if DEBUG
                    result.Append(v.Key[SqlConfiguration.ConfigVarPrefix.Length ..]).Append(" = ").AppendLine(v.Value);
#else
                    result.AppendLine(v.Key[(SqlConfiguration.ConfigVarPrefix.Length)..]);
#endif
                }
                await ctx.RespondAsync(result.ToString(), ephemeral: true).ConfigureAwait(false);
            }
            else
                await ctx.RespondAsync("No variables were set yet", ephemeral: true).ConfigureAwait(false);
        }

        [Command("set")]
        [Description("Set configuration variable value")]
        public static async ValueTask Set(
            SlashCommandContext ctx,
            [SlashAutoCompleteProvider<BotConfigurationAutoCompleteProvider>] string key,
            string value
        )
        {
            Config.InMemorySettings[key] = value;
            Config.RebuildConfiguration();
            key = SqlConfiguration.ConfigVarPrefix + key;
            await using var db = BotDb.OpenRead();
            var stateValue = await db.BotState.FirstOrDefaultAsync(v => v.Key == key).ConfigureAwait(false);
            if (stateValue == null)
            {
                stateValue = new() {Key = key, Value = value};
                await db.BotState.AddAsync(stateValue).ConfigureAwait(false);
            }
            else
                stateValue.Value = value;
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} Successfully set variable value", ephemeral: true).ConfigureAwait(false);
        }

        [Command("clear"), TextAlias("unset", "remove", "reset")]
        [Description("Removes configuration variable")]
        public static async ValueTask Clear(
            SlashCommandContext ctx,
            [SlashAutoCompleteProvider<BotConfigurationAutoCompleteProvider>] string key
        )
        {
            Config.InMemorySettings.TryRemove(key, out _);
            Config.RebuildConfiguration();
            key = SqlConfiguration.ConfigVarPrefix + key;
            await using var db = BotDb.OpenRead();
            var stateValue = await db.BotState.Where(v => v.Key == key).FirstOrDefaultAsync().ConfigureAwait(false);
            if (stateValue is not null)
            {
                db.BotState.Remove(stateValue);
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await ctx.RespondAsync($"{Config.Reactions.Success} Reset variable to default", ephemeral: true).ConfigureAwait(false);
        }
    }
}
