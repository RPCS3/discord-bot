using System.Collections.Generic;
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
    [Group("piracy"), RequiresBotModRole, RequiresDm, TriggersTyping]
    [Description("Used to manage piracy filters **in DM**")]
    internal sealed class Antipiracy: BaseCommandModuleCustom
    {
        [Command("list"), Aliases("show")]
        [Description("Lists all filters")]
        public async Task List(CommandContext ctx)
        {
            var result = new StringBuilder("```")
                .AppendLine("ID   | Trigger")
                .AppendLine("-----------------------------");
            using (var db = new BotDb())
                foreach (var item in await db.Piracystring.ToListAsync().ConfigureAwait(false))
                    result.AppendLine($"{item.Id:0000} | {item.String}");
            await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
        }

        [Command("add")]
        [Description("Adds a new piracy filter trigger")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
        {
            var wasSuccessful = await PiracyStringProvider.AddAsync(trigger).ConfigureAwait(false);
            if (wasSuccessful)
                await ctx.ReactWithAsync(Config.Reactions.Success, "New trigger successfully saved!").ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Trigger already defined.").ConfigureAwait(false);
            if (wasSuccessful)
                await List(ctx).ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del")]
        [Description("Removes a piracy filter trigger")]
        public async Task Remove(CommandContext ctx, [Description("Filter ids to remove separated with spaces")] params int[] ids)
        {
            var failedIds = new List<int>();
            foreach (var id in ids)
                if (!await PiracyStringProvider.RemoveAsync(id).ConfigureAwait(false))
                    failedIds.Add(id);
            if (failedIds.Count > 0)
                await ctx.RespondAsync("Some ids couldn't be removed: " + string.Join(", ", failedIds)).ConfigureAwait(false);
            else
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Trigger{(ids.Length == 1 ? "" : "s")} successfully removed!").ConfigureAwait(false);
            await List(ctx).ConfigureAwait(false);
        }
    }
}
