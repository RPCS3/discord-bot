using System;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("filters"), Aliases("piracy", "filter"), RequiresBotSudoerRole, RequiresDm]
    [Description("Used to manage piracy filters **in DM**")]
    internal sealed class Antipiracy: BaseCommandModuleCustom
    {
        [Command("list"), Aliases("show")]
        [Description("Lists all filters")]
        public async Task List(CommandContext ctx)
        {
            var table = new AsciiTable(
                new AsciiColumn("ID", alignToRight: true),
                new AsciiColumn("Trigger"),
                new AsciiColumn("Validation"),
                new AsciiColumn("Context"),
                new AsciiColumn("Actions"),
                new AsciiColumn("Custom message")
            );
            using (var db = new BotDb())
                foreach (var item in await db.Piracystring.Where(ps => !ps.Disabled).OrderBy(ps => ps.String).ToListAsync().ConfigureAwait(false))
                {
                    table.Add(
                        item.Id.ToString(),
                        item.String.Sanitize(),
                        item.ValidatingRegex,
                        item.Context.ToString(),
                        item.Actions.ToFlagsString(),
                        string.IsNullOrEmpty(item.CustomMessage) ? "" : "✅"
                    );
                }
            await ctx.SendAutosplitMessageAsync(table.ToString()).ConfigureAwait(false);
            await ctx.RespondAsync(FilterActionExtensions.GetLegend()).ConfigureAwait(false);
        }

        [Command("add")]
        [Description("Adds a new piracy filter trigger")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
        {
            throw new NotImplementedException();

            var wasSuccessful = false;
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
            var removedFilters = 0;
            using (var db = new BotDb())
            {
                foreach (var f in db.Piracystring.Where(ps => ids.Contains(ps.Id)))
                    f.Disabled = true;
                removedFilters = await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }

            if (removedFilters < ids.Length)
                await ctx.RespondAsync("Some ids couldn't be removed.").ConfigureAwait(false);
            else
            {
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Trigger{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
                var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                var s = removedFilters == 1 ? "" : "s";
                await ctx.Client.ReportAsync($"🤬 Piracy filter{s} removed", $"{member.GetMentionWithNickname()} removed {removedFilters} piracy filter{s}.", null, ReportSeverity.Medium).ConfigureAwait(false);
            }
            await List(ctx).ConfigureAwait(false);
        }
    }
}
