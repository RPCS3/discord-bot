using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.CommandsNext.Converters;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Commands
{
    [Group("explain"), Aliases("botsplain", "define")]
    [Cooldown(1, 3, CooldownBucketType.Channel)]
    [Description("Used to manage and show explanations")]
    internal sealed class Explain: BaseCommandModuleCustom
    {
        private const string TermListTitle = "Defined terms";

        [GroupCommand]
        public async Task ShowExplanation(CommandContext ctx, [RemainingText, Description("Term to explain")] string term)
        {
            var sourceTerm = term;
            if (string.IsNullOrEmpty(term))
            {
                var lastBotMessages = await ctx.Channel.GetMessagesBeforeAsync(ctx.Message.Id, 10).ConfigureAwait(false);
                var showList = true;
                foreach (var pastMsg in lastBotMessages)
                    if (pastMsg.Embeds.FirstOrDefault() is DiscordEmbed pastEmbed
                        && pastEmbed.Title == TermListTitle
                        || BotReactionsHandler.NeedToSilence(pastMsg).needToChill)
                    {
                        showList = false;
                        break;
                    }
                if (showList)
                    await List(ctx).ConfigureAwait(false);
                var botMsg = await ctx.RespondAsync("Please tell what term to explain:").ConfigureAwait(false);
                var interact = ctx.Client.GetInteractivity();
                var newMessage = await interact.WaitForMessageAsync(m => m.Author == ctx.User && m.Channel == ctx.Channel && !string.IsNullOrEmpty(m.Content)).ConfigureAwait(false);
                await botMsg.DeleteAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(newMessage.Result?.Content) || newMessage.Result.Content.StartsWith(Config.CommandPrefix))
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure).ConfigureAwait(false);
                    return;
                }

                sourceTerm = term = newMessage.Result.Content;
            }

            if (!await DiscordInviteFilter.CheckMessageForInvitesAsync(ctx.Client, ctx.Message).ConfigureAwait(false))
                return;

            if (!await ContentFilter.IsClean(ctx.Client, ctx.Message).ConfigureAwait(false))
                return;

            term = term.ToLowerInvariant();
            var result = await LookupTerm(term).ConfigureAwait(false);
            if (result.explanation == null || !string.IsNullOrEmpty(result.fuzzyMatch))
            {
                term = term.StripQuotes();
                var idx = term.LastIndexOf(" to ");
                if (idx > 0)
                {
                    var potentialUserId = term[(idx + 4)..].Trim();
                    bool hasMention = false;
                    try
                    {
                        var lookup = await ((IArgumentConverter<DiscordUser>)new DiscordUserConverter()).ConvertAsync(potentialUserId, ctx).ConfigureAwait(false);
                        hasMention = lookup.HasValue;
                    }
                    catch {}

                    if (hasMention)
                    {
                        term = term[..idx].TrimEnd();
                        var mentionResult = await LookupTerm(term).ConfigureAwait(false);
                        if (mentionResult.score > result.score)
                            result = mentionResult;
                    }
                }
            }

            if (await SendExplanation(result, term, ctx.Message).ConfigureAwait(false))
                return;

            string inSpecificLocation = null;
            if (!LimitedToSpamChannel.IsSpamChannel(ctx.Channel))
            {
                var spamChannel = await ctx.Client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
                inSpecificLocation = $" in {spamChannel.Mention} or bot DMs";
            }
            var msg = $"Unknown term `{term.Sanitize(replaceBackTicks: true)}`. Use `{ctx.Prefix}explain list` to look at defined terms{inSpecificLocation}";
            await ctx.RespondAsync(msg).ConfigureAwait(false);
        }

        [Command("add"), RequiresBotModRole]
        [Description("Adds a new explanation to the list")]
        public async Task Add(CommandContext ctx,
            [Description("A term to explain. Quote it if it contains spaces")] string term,
            [RemainingText, Description("Explanation text. Can have attachment")] string explanation)
        {
            try
            {
                term = term.ToLowerInvariant().StripQuotes();
                byte[] attachment = null;
                string attachmentFilename = null;
                if (ctx.Message.Attachments.FirstOrDefault() is DiscordAttachment att)
                {
                    attachmentFilename = att.FileName;
                    try
                    {
                        using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                        attachment = await httpClient.GetByteArrayAsync(att.Url).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Config.Log.Warn(e, "Failed to download explanation attachment " + ctx);
                    }
                }

                if (string.IsNullOrEmpty(explanation) && string.IsNullOrEmpty(attachmentFilename))
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "An explanation for the term must be provided").ConfigureAwait(false);
                else
                {
                    using var db = new BotDb();
                    if (await db.Explanation.AnyAsync(e => e.Keyword == term).ConfigureAwait(false))
                        await ctx.ReactWithAsync(Config.Reactions.Failure, $"`{term}` is already defined. Use `update` to update an existing term.").ConfigureAwait(false);
                    else
                    {
                        var entity = new Explanation
                        {
                            Keyword = term, Text = explanation ?? "", Attachment = attachment,
                            AttachmentFilename = attachmentFilename
                        };
                        await db.Explanation.AddAsync(entity).ConfigureAwait(false);
                        await db.SaveChangesAsync().ConfigureAwait(false);
                        await ctx.ReactWithAsync(Config.Reactions.Success, $"`{term}` was successfully added").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, $"Failed to add explanation for `{term}`");
            }
        }

        [Command("update"), Aliases("replace"), RequiresBotModRole]
        [Description("Update explanation for a given term")]
        public async Task Update(CommandContext ctx,
            [Description("A term to update. Quote it if it contains spaces")] string term,
            [RemainingText, Description("New explanation text")] string explanation)
        {
            term = term.ToLowerInvariant().StripQuotes();
            byte[] attachment = null;
            string attachmentFilename = null;
            if (ctx.Message.Attachments.FirstOrDefault() is DiscordAttachment att)
            {
                attachmentFilename = att.FileName;
                try
                {
                    using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                    attachment = await httpClient.GetByteArrayAsync(att.Url).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to download explanation attachment " + ctx);
                }
            }
            using var db = new BotDb();
            var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            if (item == null)
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` is not defined").ConfigureAwait(false);
            else
            {
                if (!string.IsNullOrEmpty(explanation))
                    item.Text = explanation;
                if (attachment?.Length > 0)
                {
                    item.Attachment = attachment;
                    item.AttachmentFilename = attachmentFilename;
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, "Term was updated").ConfigureAwait(false);
            }
        }

        [Command("rename"), Priority(10), RequiresBotModRole]
        public async Task Rename(CommandContext ctx,
            [Description("A term to rename. Remember quotes if it contains spaces")] string oldTerm,
            [Description("New term. Again, quotes")] string newTerm)
        {
            oldTerm = oldTerm.ToLowerInvariant().StripQuotes();
            newTerm = newTerm.ToLowerInvariant().StripQuotes();
            using var db = new BotDb();
            var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == oldTerm).ConfigureAwait(false);
            if (item == null)
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{oldTerm}` is not defined").ConfigureAwait(false);
            else if (await db.Explanation.AnyAsync(e => e.Keyword == newTerm).ConfigureAwait(false))
                await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{newTerm}` already defined, can't replace it with explanation for `{oldTerm}`").ConfigureAwait(false);
            else
            {
                item.Keyword = newTerm;
                await db.SaveChangesAsync().ConfigureAwait(false);
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Renamed `{oldTerm}` to `{newTerm}`").ConfigureAwait(false);
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

        [Command("list")]
        [Description("List all known terms that could be used for !explain command")]
        public async Task List(CommandContext ctx)
        {
            var responseChannel = await ctx.GetChannelForSpamAsync().ConfigureAwait(false);
            using var db = new BotDb();
            var keywords = await db.Explanation.Select(e => e.Keyword).OrderBy(t => t).ToListAsync().ConfigureAwait(false);
            if (keywords.Count == 0)
                await ctx.RespondAsync("Nothing has been defined yet").ConfigureAwait(false);
            else
                try
                {
                    foreach (var embed in keywords.BreakInEmbeds(new DiscordEmbedBuilder {Title = TermListTitle, Color = Config.Colors.Help}))
                        await responseChannel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Error(e);
                }
        }

        [Group("remove"), Aliases("delete", "del", "erase", "obliterate"), RequiresBotModRole]
        [Description("Removes an explanation from the definition list")]
        internal sealed class Remove: BaseCommandModuleCustom
        {
            [GroupCommand]
            public async Task RemoveExplanation(CommandContext ctx, [RemainingText, Description("Term to remove")] string term)
            {
                term = term.ToLowerInvariant().StripQuotes();
                using var db = new BotDb();
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

            [Command("attachment"), Aliases("image", "picture", "file")]
            [Description("Removes attachment from specified explanation. If there is no text, the whole explanation is removed")]
            public async Task Attachment(CommandContext ctx, [RemainingText, Description("Term to remove")] string term)
            {
                term = term.ToLowerInvariant().StripQuotes();
                using var db = new BotDb();
                var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                if (item == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` is not defined").ConfigureAwait(false);
                else if (string.IsNullOrEmpty(item.AttachmentFilename))
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` doesn't have any attachments").ConfigureAwait(false);
                else if (string.IsNullOrEmpty(item.Text))
                    await RemoveExplanation(ctx, term).ConfigureAwait(false);
                else
                {
                    item.Attachment = null;
                    item.AttachmentFilename = null;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Removed attachment for `{term}`").ConfigureAwait(false);
                }
            }

            [Command("text"), Aliases("description")]
            [Description("Removes explanation text. If there is no attachment, the whole explanation is removed")]
            public async Task Text(CommandContext ctx, [RemainingText, Description("Term to remove")] string term)
            {
                term = term.ToLowerInvariant().StripQuotes();
                using var db = new BotDb();
                var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
                if (item == null)
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` is not defined").ConfigureAwait(false);
                else if (string.IsNullOrEmpty(item.Text))
                    await ctx.ReactWithAsync(Config.Reactions.Failure, $"Term `{term}` doesn't have any text").ConfigureAwait(false);
                else if (string.IsNullOrEmpty(item.AttachmentFilename))
                    await RemoveExplanation(ctx, term).ConfigureAwait(false);
                else
                {
                    item.Text = "";
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await ctx.ReactWithAsync(Config.Reactions.Success, $"Removed explanation text for `{term}`").ConfigureAwait(false);
                }
            }
        }

        [Command("dump"), Aliases("download")]
        [Description("Returns explanation text as a file attachment")]
        public async Task Dump(CommandContext ctx, [RemainingText, Description("Term to dump **or** a link to a message containing the explanation")] string termOrLink = null)
        {
            if (string.IsNullOrEmpty(termOrLink))
            {
                var term = ctx.Message.Content.Split(' ', 2).Last();
                await ShowExplanation(ctx, term).ConfigureAwait(false);
                return;
            }

            if (!await DiscordInviteFilter.CheckMessageForInvitesAsync(ctx.Client, ctx.Message).ConfigureAwait(false))
                return;

            termOrLink = termOrLink.ToLowerInvariant().StripQuotes();
            var isLink = CommandContextExtensions.MessageLinkRegex.IsMatch(termOrLink);
            if (isLink)
            {
                await DumpLink(ctx, termOrLink).ConfigureAwait(false);
                return;
            }

            using var db = new BotDb();
            var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == termOrLink).ConfigureAwait(false);
            if (item == null)
            {
                var term = ctx.Message.Content.Split(' ', 2).Last();
                await ShowExplanation(ctx, term).ConfigureAwait(false);
            }
            else
            {
                if (!string.IsNullOrEmpty(item.Text))
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(item.Text));
                    await ctx.Channel.SendFileAsync($"{termOrLink}.txt", stream).ConfigureAwait(false);
                }
                if (!string.IsNullOrEmpty(item.AttachmentFilename))
                {
                    using var stream = new MemoryStream(item.Attachment);
                    await ctx.Channel.SendFileAsync(item.AttachmentFilename, stream).ConfigureAwait(false);
                }
            }
        }

        internal static async Task<(Explanation explanation, string fuzzyMatch, double score)> LookupTerm(string term)
        {
            using var db = new BotDb();
            string fuzzyMatch = null;
            double coefficient;
            var explanation = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
            if (explanation == null)
            {
                var termList = await db.Explanation.Select(e => e.Keyword).ToListAsync().ConfigureAwait(false);
                var bestSuggestion = termList.OrderByDescending(term.GetFuzzyCoefficientCached).First();
                coefficient = term.GetFuzzyCoefficientCached(bestSuggestion);
                explanation = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == bestSuggestion).ConfigureAwait(false);
                fuzzyMatch = bestSuggestion;
            }
            else
                coefficient = 2.0;
            return (explanation, fuzzyMatch, coefficient);
        }

        internal static async Task<bool> SendExplanation((Explanation explanation, string fuzzyMatch, double score) termLookupResult, string term, DiscordMessage sourceMessage)
        {
            try
            {
                if (termLookupResult.explanation != null && termLookupResult.score > 0.5)
                {
                    if (!string.IsNullOrEmpty(termLookupResult.fuzzyMatch))
                    {
                        var fuzzyNotice = $"Showing explanation for `{termLookupResult.fuzzyMatch}`:";
#if DEBUG
                        fuzzyNotice = $"Showing explanation for `{termLookupResult.fuzzyMatch}` ({termLookupResult.score:0.######}):";
#endif
                        await sourceMessage.Channel.SendMessageAsync(fuzzyNotice).ConfigureAwait(false);
                    }

                    var explain = termLookupResult.explanation;
                    StatsStorage.ExplainStatCache.TryGetValue(explain.Keyword, out int stat);
                    StatsStorage.ExplainStatCache.Set(explain.Keyword, ++stat, StatsStorage.CacheTime);
                    await sourceMessage.Channel.SendMessageAsync(explain.Text, explain.Attachment, explain.AttachmentFilename).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to explain " + term);
                return true;
            }
            return false;
        }

        private static async Task DumpLink(CommandContext ctx, string messageLink)
        {
            string explanation = null;
            DiscordMessage msg = null;
            try { msg = await ctx.GetMessageAsync(messageLink).ConfigureAwait(false); } catch {}
            if (msg != null)
            {
                if (msg.Embeds.FirstOrDefault() is DiscordEmbed embed && !string.IsNullOrEmpty(embed.Description))
                    explanation = embed.Description;
                else if (!string.IsNullOrEmpty(msg.Content))
                    explanation = msg.Content;
            }

            if (string.IsNullOrEmpty(explanation))
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Couldn't find any text in the specified message").ConfigureAwait(false);
            else
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(explanation));
                await ctx.Channel.SendFileAsync("explanation.txt", stream).ConfigureAwait(false);
            }
        }
    }
}
