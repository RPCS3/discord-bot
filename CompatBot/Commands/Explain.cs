using System;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("explain"), Aliases("botsplain", "define")]
    //[Cooldown(1, 1, CooldownBucketType.Channel)]
    [Description("Used to manage and show explanations")]
    internal sealed class Explain: BaseCommandModule
    {
        [GroupCommand]
        public async Task ShowExplanation(CommandContext ctx, [RemainingText, Description("Term to explain")] string term)
        {
            await ctx.TriggerTypingAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(term))
            {
                await ctx.RespondAsync($"You may want to look at available terms by using `{Config.CommandPrefix}explain list`").ConfigureAwait(false);
                return;
            }

            var explanation = await BotDb.Instance.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            if (explanation != null)
            {
                await ctx.RespondAsync(explanation.Text).ConfigureAwait(false);
                return;
            }

            term = term.StripQuotes();
            var idx = term.LastIndexOf(" to ", StringComparison.InvariantCultureIgnoreCase);
            if (idx > 0)
            {
                var potentialUserId = term.Substring(idx + 4).Trim();
                bool hasMention = false;
                try
                {
                    var lookup = await new DiscordUserConverter().ConvertAsync(potentialUserId, ctx).ConfigureAwait(false);
                    hasMention = lookup.HasValue;
                }
                catch { }
                if (hasMention)
                {
                    term = term.Substring(0, idx).TrimEnd();
                    explanation = await BotDb.Instance.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                    if (explanation != null)
                    {
                        await ctx.RespondAsync(explanation.Text).ConfigureAwait(false);
                        return;
                    }
                }
            }

            await ctx.Message.CreateReactionAsync(Config.Reactions.Failure).ConfigureAwait(false);
        }

        [Command("add")]
        [Description("Adds a new explanation to the list")]
        public async Task Add(CommandContext ctx,
            [Description("A term to explain. Quote it if it contains spaces")] string term,
            [RemainingText, Description("Explanation text")] string explanation)
        {
            term = term.StripQuotes();
            if (string.IsNullOrEmpty(explanation))
            {
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Failure),
                    ctx.RespondAsync("An explanation for the term must be provided")
                ).ConfigureAwait(false);
            }
            else if (await BotDb.Instance.Explanation.AnyAsync(e => e.Keyword == term).ConfigureAwait(false))
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Failure),
                    ctx.RespondAsync($"'{term}' is already defined. Use `update` to update an existing term.")
                ).ConfigureAwait(false);
            else
            {
                await BotDb.Instance.Explanation.AddAsync(new Explanation {Keyword = term, Text = explanation}).ConfigureAwait(false);
                await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
        }

        [Command("update"), Aliases("replace"), RequiresBotModRole]
        [Description("Update explanation for a given term")]
        public async Task Update(CommandContext ctx,
            [Description("A term to update. Quote it if it contains spaces")] string term,
            [RemainingText, Description("New explanation text")] string explanation)
        {
            term = term.StripQuotes();
            var item = await BotDb.Instance.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            if (item == null)
            {
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Failure),
                    ctx.RespondAsync($"Term '{term}' is not defined")
                ).ConfigureAwait(false);
            }
            else
            {
                item.Text = explanation;
                await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
        }

        [Command("rename"), Priority(10), RequiresBotModRole]
        public async Task Rename(CommandContext ctx,
            [Description("A term to rename. Remember quotes if it contains spaces")] string oldTerm,
            [Description("New term. Again, quotes")] string newTerm)
        {
            oldTerm = oldTerm.StripQuotes();
            newTerm = newTerm.StripQuotes();
            var item = await BotDb.Instance.Explanation.FirstOrDefaultAsync(e => e.Keyword == oldTerm).ConfigureAwait(false);
            if (item == null)
            {
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Failure),
                    ctx.RespondAsync($"Term '{oldTerm}' is not defined")
                ).ConfigureAwait(false);
            }
            else
            {
                item.Keyword = newTerm;
                await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
        }

        [Command("rename"), Priority(1), RequiresBotModRole]
        [Description("Renames a term in case you misspelled it or something")]
        public async Task Rename(CommandContext ctx,
            [Description("A term to rename. Remember quotes if it contains spaces")] string oldTerm,
            [Description("Constant \"to'")] string to,
            [Description("New term. Again, quotes")] string newTerm)
        {
            if ("to".Equals(to, StringComparison.InvariantCultureIgnoreCase))
                await Rename(ctx, oldTerm, newTerm).ConfigureAwait(false);
            else
                await ctx.Message.CreateReactionAsync(Config.Reactions.Failure).ConfigureAwait(false);
        }

        [Command("list")]
        [Description("List all known terms that could be used for !explain command")]
        public async Task List(CommandContext ctx)
        {
            await ctx.TriggerTypingAsync().ConfigureAwait(false);
            var keywords = await BotDb.Instance.Explanation.Select(e => e.Keyword).OrderBy(t => t).ToListAsync().ConfigureAwait(false);
            if (keywords.Count == 0)
                await ctx.RespondAsync("Nothing has been defined yet").ConfigureAwait(false);
            else
                try
                {
                    foreach (var embed in new EmbedPager().BreakInEmbeds(new DiscordEmbedBuilder {Title = "Defined terms", Color = Config.Colors.Help}, keywords))
                        await ctx.RespondAsync(embed: embed).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ctx.Client.DebugLogger.LogMessage(LogLevel.Error, "", e.ToString(), DateTime.Now);
                }
        }


        [Command("remove"), Aliases("delete", "del"), RequiresBotModRole]
        [Description("Removes an explanation from the definition list")]
        public async Task Remove(CommandContext ctx, [RemainingText, Description("Term to remove")] string term)
        {
            term = term.StripQuotes();
            var item = await BotDb.Instance.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            if (item == null)
            {
                await Task.WhenAll(
                    ctx.Message.CreateReactionAsync(Config.Reactions.Failure),
                    ctx.RespondAsync($"Term '{term}' is not defined")
                ).ConfigureAwait(false);
            }
            else
            {
                BotDb.Instance.Explanation.Remove(item);
                await BotDb.Instance.SaveChangesAsync().ConfigureAwait(false);
                await ctx.Message.CreateReactionAsync(Config.Reactions.Success).ConfigureAwait(false);
            }
        }

    }
}
