using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("piracy"), RequiresBotModRole, RequiresDm]
    [Description("Used to manage piracy filters **in DM**")]
    internal sealed class Antipiracy: BaseCommandModule
    {
        [Command("list"), Aliases("show")]
        [Description("Lists all filters")]
        public async Task List(CommandContext ctx)
        {
            var typingTask = ctx.TriggerTypingAsync();
            var result = new StringBuilder("```")
                .AppendLine("ID   | Trigger")
                .AppendLine("-----------------------------");
            using (var db = new BotDb())
                foreach (var item in await db.Piracystring.ToListAsync().ConfigureAwait(false))
                    result.AppendLine($"{item.Id:0000} | {item.String}");
            await ctx.SendAutosplitMessageAsync(result.Append("```")).ConfigureAwait(false);
            await typingTask;
        }

        [Command("add")]
        [Description("Adds a new piracy filter trigger")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
        {
            var typingTask = ctx.TriggerTypingAsync();
            var wasSuccessful = await PiracyStringProvider.AddAsync(trigger).ConfigureAwait(false);
            (DiscordEmoji reaction, string msg) result = wasSuccessful
                ? (Config.Reactions.Success, "New trigger successfully saved!")
                : (Config.Reactions.Failure, "Trigger already defined.");
            await Task.WhenAll(
                ctx.RespondAsync(result.msg),
                ctx.Message.CreateReactionAsync(result.reaction),
                typingTask
            ).ConfigureAwait(false);
            if (wasSuccessful)
                await List(ctx).ConfigureAwait(false);
        }

        [Command("remove"), Aliases("delete", "del")]
        [Description("Removes a piracy filter trigger")]
        public async Task Remove(CommandContext ctx, [Description("Filter ids to remove separated with spaces")] params int[] ids)
        {
            var typingTask = ctx.TriggerTypingAsync();
            (DiscordEmoji reaction, string msg) result = (Config.Reactions.Success, $"Trigger{(ids.Length == 1 ? "" : "s")} successfully removed!");
            var failedIds = new List<int>();
            foreach (var id in ids)
                if (!await PiracyStringProvider.RemoveAsync(id).ConfigureAwait(false))
                    failedIds.Add(id);
            if (failedIds.Count > 0)
                result = (Config.Reactions.Failure, "Some ids couldn't be removed: " + string.Join(", ", failedIds));
            await Task.WhenAll(
                ctx.RespondAsync(result.msg),
                ctx.Message.CreateReactionAsync(result.reaction),
                typingTask
            ).ConfigureAwait(false);
            await List(ctx).ConfigureAwait(false);
        }
    }
}
