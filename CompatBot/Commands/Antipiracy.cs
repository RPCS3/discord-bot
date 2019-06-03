using System.Collections.Generic;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("piracy"), RequiresBotSudoerRole, RequiresDm]
    [Description("Used to manage piracy filters **in DM**")]
    internal sealed class Antipiracy: BaseCommandModuleCustom
    {
        [Command("list"), Aliases("show")]
        [Description("Lists all filters")]
        public async Task List(CommandContext ctx)
        {
            var table = new AsciiTable(
                new AsciiColumn("ID", alignToRight: true),
                new AsciiColumn("Trigger")
            );
            using (var db = new BotDb())
                foreach (var item in await db.Piracystring.ToListAsync().ConfigureAwait(false))
                    table.Add(item.Id.ToString(), item.String);
            await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
        }

        [Command("add")]
        [Description("Adds a new piracy filter trigger")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
        {
            var wasSuccessful = await PiracyStringProvider.AddAsync(trigger).ConfigureAwait(false);
            if (wasSuccessful)
            {
                await ctx.ReactWithAsync(Config.Reactions.Success, "New trigger successfully saved!").ConfigureAwait(false);
                var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                await ctx.Client.ReportAsync("🤬 Piracy filter added", $"{member.GetMentionWithNickname()} added a new piracy filter:\n```{trigger.Sanitize()}```", null, ReportSeverity.Low).ConfigureAwait(false);
            }
            else
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Trigger already defined.").ConfigureAwait(false);
            if (wasSuccessful)
                await List(ctx).ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del")]
        [Description("Removes a piracy filter trigger")]
        public async Task Remove(CommandContext ctx, [Description("Filter IDs to remove, separated with spaces")] params int[] ids)
        {
            var failedIds = new List<int>();
            var removedFilters = new List<string>();
            foreach (var id in ids)
            {
                var trigger = await PiracyStringProvider.GetTriggerAsync(id).ConfigureAwait(false);
                if (await PiracyStringProvider.RemoveAsync(id).ConfigureAwait(false))
                    removedFilters.Add(trigger.Sanitize());
                else
                    failedIds.Add(id);
            }

            if (failedIds.Count > 0)
                await ctx.RespondAsync("Some ids couldn't be removed: " + string.Join(", ", failedIds)).ConfigureAwait(false);
            else
            {
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Trigger{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
                var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                var s = removedFilters.Count == 1 ? "" : "s";
                await ctx.Client.ReportAsync($"🤬 Piracy filter{s} removed", $"{member.GetMentionWithNickname()} removed {removedFilters.Count} piracy filter{s}:\n```\n{string.Join('\n', removedFilters)}\n```", null, ReportSeverity.Medium).ConfigureAwait(false);
            }
            await List(ctx).ConfigureAwait(false);
        }
    }
}
