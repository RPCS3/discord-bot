using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    internal partial class Sudo
    {
        public sealed partial class Bot
        {
            [Group("config"), RequiresBotSudoerRole]
            [Description("Commands to set or clear bot configuration variables")]
            public sealed class Configuration : BaseCommandModule
            {
                [Command("list"), Aliases("show")]
                [Description("Lists set variable names")]
                public async Task List(CommandContext ctx)
                {
                    await using var db = new BotDb();
                    var setVars = await db.BotState.AsNoTracking().Where(v => v.Key.StartsWith(SqlConfiguration.ConfigVarPrefix)).ToListAsync().ConfigureAwait(false);
                    if (setVars.Any())
                    {
                        var result = new StringBuilder("Set variables:").AppendLine();
                        foreach (var v in setVars)
                        {
#if DEBUG
                            result.Append(v.Key![SqlConfiguration.ConfigVarPrefix.Length ..]).Append(" = ").AppendLine(v.Value);
#else
                            result.AppendLine(v.Key![(SqlConfiguration.ConfigVarPrefix.Length)..]);
#endif
                        }
                        await ctx.RespondAsync(result.ToString()).ConfigureAwait(false);
                    }
                    else
                        await ctx.RespondAsync("No variables were set yet").ConfigureAwait(false);
                }

                [Command("set")]
                [Description("Sets configuration variable")]
                public async Task Set(CommandContext ctx, string key, [RemainingText] string value)
                {
                    Config.InMemorySettings[key] = value;
                    Config.RebuildConfiguration();
                    key = SqlConfiguration.ConfigVarPrefix + key;
                    await using var db = new BotDb();
                    var stateValue = await db.BotState.Where(v => v.Key == key).FirstOrDefaultAsync().ConfigureAwait(false);
                    if (stateValue == null)
                    {
                        stateValue = new BotState {Key = key, Value = value};
                        await db.BotState.AddAsync(stateValue).ConfigureAwait(false);
                    }
                    else
                        stateValue.Value = value;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, "Set variable successfully").ConfigureAwait(false);
                }

                [Command("clear"), Aliases("unset", "remove")]
                [Description("Removes configuration variable")]
                public async Task Clear(CommandContext ctx, string key)
                {
                    Config.InMemorySettings.TryRemove(key, out _);
                    Config.RebuildConfiguration();
                    key = SqlConfiguration.ConfigVarPrefix + key;
                    await using var db = new BotDb();
                    var stateValue = await db.BotState.Where(v => v.Key == key).FirstOrDefaultAsync().ConfigureAwait(false);
                    if (stateValue != null)
                    {
                        db.BotState.Remove(stateValue);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    await ctx.ReactWithAsync(Config.Reactions.Success, "Removed variable successfully").ConfigureAwait(false);
                }
            }
        }
    }
}