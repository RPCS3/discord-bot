using System;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands
{
    [Group("explain"), Aliases("botsplain", "define")]
    [Cooldown(1, 3, CooldownBucketType.Channel)]
    [Description("Used to manage and show explanations")]
    internal sealed class Explain: BaseCommandModuleCustom
    {
        [GroupCommand]
        public async Task ShowExplanation(CommandContext ctx, [RemainingText, Description("Term to explain")] string term)
        {
            await ctx.TriggerTypingAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(term))
            {
                var spamChannel = await ctx.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
                await ctx.RespondAsync($"You may want to look at available terms by using `{Config.CommandPrefix}explain list` in {spamChannel.Mention} or bot DMs").ConfigureAwait(false);
                return;
            }

            term = term.ToLowerInvariant();
            using (var db = new BotDb())
            {
                var explanation = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                if (explanation != null)
                {
                    await ctx.RespondAsync(explanation.Text).ConfigureAwait(false);
                    return;
                }
            }

            term = term.StripQuotes();
            var idx = term.LastIndexOf(" to ");
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
                    using (var db = new BotDb())
                    {
                        var explanation = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                        if (explanation != null)
                        {
                            await ctx.RespondAsync(explanation.Text).ConfigureAwait(false);
                            return;
                        }
                    }
                }
            }

            var spamCh = await ctx.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
            await ctx.RespondAsync($"Unknown term `{term.Sanitize()}`. Use `!explain list` to look at defined terms in {spamCh.Mention} or bot DMs").ConfigureAwait(false);
        }

        [Command("add"), RequiresBotModRole]
        [Description("Adds a new explanation to the list")]
        public async Task Add(CommandContext ctx,
            [Description("A term to explain. Quote it if it contains spaces")] string term,
            [RemainingText, Description("Explanation text")] string explanation)
        {
            term = term.ToLowerInvariant().StripQuotes();
            if (string.IsNullOrEmpty(explanation))
                await ctx.ReactWithAsync(Config.Reactions.Failure, "An explanation for the term must be provided").ConfigureAwait(false);
            else
            {
                using (var db = new BotDb())
                {
                    if (await db.Explanation.AnyAsync(e => e.Keyword == term).ConfigureAwait(false))
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{term}` is already defined. Use `update` to update an existing term.").ConfigureAwait(false);
                    else
                    {
                        await db.Explanation.AddAsync(new Explanation {Keyword = term, Text = explanation}).ConfigureAwait(false);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                        await ctx.ReactWithAsync(Config.Reactions.Success, $"`{term}` was successfully added").ConfigureAwait(false);
                    }
                }
            }
        }

        [Command("update"), Aliases("replace"), RequiresBotModRole]
        [Description("Update explanation for a given term")]
        public async Task Update(CommandContext ctx,
            [Description("A term to update. Quote it if it contains spaces")] string term,
            [RemainingText, Description("New explanation text")] string explanation)
        {
            term = term.ToLowerInvariant().StripQuotes();
            using (var db = new BotDb())
            {
                var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                if (item == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` is not defined").ConfigureAwait(false);
                else
                {
                    item.Text = explanation;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, "Term was updated").ConfigureAwait(false);
                }
            }
        }

        [Command("rename"), Priority(10), RequiresBotModRole]
        public async Task Rename(CommandContext ctx,
            [Description("A term to rename. Remember quotes if it contains spaces")] string oldTerm,
            [Description("New term. Again, quotes")] string newTerm)
        {
            oldTerm = oldTerm.ToLowerInvariant().StripQuotes();
            newTerm = newTerm.ToLowerInvariant().StripQuotes();
            using (var db = new BotDb())
            {
                var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == oldTerm).ConfigureAwait(false);
                if (item == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{oldTerm}` is not defined").ConfigureAwait(false);
                else
                {
                    item.Keyword = newTerm;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed `{oldTerm}` to `{newTerm}`").ConfigureAwait(false);
                }
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
                await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
        }

        [Command("list"), LimitedToSpamChannel, TriggersTyping]
        [Description("List all known terms that could be used for !explain command")]
        public async Task List(CommandContext ctx)
        {
            using (var db = new BotDb())
            {
                var keywords = await db.Explanation.Select(e => e.Keyword).OrderBy(t => t).ToListAsync().ConfigureAwait(false);
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
                        Config.Log.Error(e);
                    }
            }
        }

        [Command("remove"), Aliases("delete", "del", "erase", "obliterate"), RequiresBotModRole]
        [Description("Removes an explanation from the definition list")]
        public async Task Remove(CommandContext ctx, [RemainingText, Description("Term to remove")] string term)
        {
            term = term.ToLowerInvariant().StripQuotes();
            using (var db = new BotDb())
            {
                var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                if (item == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` is not defined").ConfigureAwait(false);
                else
                {
                    db.Explanation.Remove(item);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Removed `{term}`").ConfigureAwait(false);
                }
            }
        }
    }
}
