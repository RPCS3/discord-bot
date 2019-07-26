using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using Exception = System.Exception;

namespace CompatBot.Commands
{
    [Group("filters"), Aliases("piracy", "filter"), RequiresBotSudoerRole, RequiresDm]
    [Description("Used to manage content filters. **Works only in DM**")]
    internal sealed class Antipiracy: BaseCommandModuleCustom
    {
        private static readonly TimeSpan InteractTimeout = TimeSpan.FromMinutes(5);
        private static readonly char[] Separators = {' ', ',', ';', '|'};

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
                foreach (var item in await db.Piracystring.Where(ps => !ps.Disabled).OrderBy(ps => ps.String.ToUpperInvariant()).ToListAsync().ConfigureAwait(false))
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

        [Command("add"), Aliases("create")]
        [Description("Adds a new content filter")]
        public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
        {
            using (var db = new BotDb())
            {
                Piracystring filter;
                if (string.IsNullOrEmpty(trigger))
                    filter = new Piracystring();
                else
                {
                    filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String == trigger).ConfigureAwait(false);
                    if (filter == null)
                        filter = new Piracystring {String = trigger};
                    else
                        filter.Disabled = false;
                }
                var isNewFilter = filter.Id == default;
                if (isNewFilter)
                {
                    filter.Context = FilterContext.Chat | FilterContext.Log;
                    filter.Actions = FilterAction.RemoveContent | FilterAction.IssueWarning | FilterAction.SendMessage;
                }

                var (success, msg) = await EditFilterPropertiesAsync(ctx, db, filter).ConfigureAwait(false);
                if (success)
                {
                    if (isNewFilter)
                        await db.Piracystring.AddAsync(filter).ConfigureAwait(false);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatFilter(filter).WithTitle("Created a new content filter")).ConfigureAwait(false);
                    var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                    await ctx.Client.ReportAsync("🆕 Content filter created", $"{member.GetMentionWithNickname()} added a new content filter: `{filter.String.Sanitize()}`", null, ReportSeverity.Low).ConfigureAwait(false);
                    ContentFilter.RebuildMatcher();
                }
                else
                    await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Content filter creation aborted").ConfigureAwait(false);
            }
        }

        [Command("edit"), Aliases("fix", "update", "change")]
        [Description("Modifies the specified content filter")]
        public async Task Edit(CommandContext ctx, [Description("Filter ID")] int id)
        {
            using (var db = new BotDb())
            {
                var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id && !ps.Disabled).ConfigureAwait(false);
                if (filter == null)
                {
                    await ctx.RespondAsync("Specified filter does not exist").ConfigureAwait(false);
                    return;
                }

                await EditFilterCmd(ctx, db, filter).ConfigureAwait(false);
            }
        }

        [Command("edit")]
        public async Task Edit(CommandContext ctx, [Description("Trigger to edit"), RemainingText] string trigger)
        {
            using (var db = new BotDb())
            {
                var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String.Equals(trigger, StringComparison.InvariantCultureIgnoreCase) && !ps.Disabled).ConfigureAwait(false);
                if (filter == null)
                {
                    await ctx.RespondAsync("Specified filter does not exist").ConfigureAwait(false);
                    return;
                }

                await EditFilterCmd(ctx, db, filter).ConfigureAwait(false);
            }
        }

        [Command("remove"), Aliases("delete", "del")]
        [Description("Removes a piracy filter trigger")]
        public async Task Remove(CommandContext ctx, [Description("Filter IDs to remove, separated with spaces")] params int[] ids)
        {
            int removedFilters;
            var removedTriggers = new StringBuilder();
            using (var db = new BotDb())
            {
                foreach (var f in db.Piracystring.Where(ps => ids.Contains(ps.Id)))
                {
                    f.Disabled = true;
                    removedTriggers.Append($"\n`{f.String.Sanitize()}`");
                }
                removedFilters = await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }

            if (removedFilters < ids.Length)
                await ctx.RespondAsync("Some ids couldn't be removed.").ConfigureAwait(false);
            else
            {
                await ctx.ReactWithAsync(Config.Reactions.Success, $"Trigger{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
                var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                var s = removedFilters == 1 ? "" : "s";
                var filterList = removedTriggers.ToString();
                if (removedFilters == 1)
                    filterList = filterList.TrimStart();
                await ctx.Client.ReportAsync($"📴 Piracy filter{s} removed", $"{member.GetMentionWithNickname()} removed {removedFilters} piracy filter{s}: {filterList}".Trim(EmbedPager.MaxDescriptionLength), null, ReportSeverity.Medium).ConfigureAwait(false);
            }
            ContentFilter.RebuildMatcher();
        }

        [Command("remove")]
        public async Task Remove(CommandContext ctx, [Description("Trigger to remove"), RemainingText] string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "No trigger was specified").ConfigureAwait(false);
                return;
            }

            using (var db = new BotDb())
            {
                var f = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String.Equals(trigger, StringComparison.InvariantCultureIgnoreCase) && !ps.Disabled).ConfigureAwait(false);
                if (f == null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Specified filter does not exist").ConfigureAwait(false);
                    return;
                }

                f.Disabled = true;
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }

            await ctx.ReactWithAsync(Config.Reactions.Success, "Trigger was removed").ConfigureAwait(false);
            var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
            await ctx.Client.ReportAsync("📴 Piracy filter removed", $"{member.GetMentionWithNickname()} removed 1 piracy filter: `{trigger.Sanitize()}`", null, ReportSeverity.Medium).ConfigureAwait(false);
            ContentFilter.RebuildMatcher();
        }

        private async Task EditFilterCmd(CommandContext ctx, BotDb db, Piracystring filter)
        {
            var (success, msg) = await EditFilterPropertiesAsync(ctx, db, filter).ConfigureAwait(false);
            if (success)
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatFilter(filter).WithTitle("Updated content filter")).ConfigureAwait(false);
                var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
                await ctx.Client.ReportAsync("🆙 Content filter updated", $"{member.GetMentionWithNickname()} changed content filter: `{filter.String.Sanitize()}`", null, ReportSeverity.Low).ConfigureAwait(false);
                ContentFilter.RebuildMatcher();
            }
            else
                await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Content filter update aborted").ConfigureAwait(false);
        }

        private async Task<(bool success, DiscordMessage message)> EditFilterPropertiesAsync(CommandContext ctx, BotDb db, Piracystring filter)
        {
            var interact = ctx.Client.GetInteractivity();
            var abort = DiscordEmoji.FromUnicode("🛑");
            var lastPage = DiscordEmoji.FromUnicode("↪");
            var firstPage = DiscordEmoji.FromUnicode("↩");
            var previousPage = DiscordEmoji.FromUnicode("⏪");
            var nextPage = DiscordEmoji.FromUnicode("⏩");
            var trash = DiscordEmoji.FromUnicode("🗑");
            var saveEdit = DiscordEmoji.FromUnicode("💾");

            var letterC = DiscordEmoji.FromUnicode("🇨");
            var letterL = DiscordEmoji.FromUnicode("🇱");
            var letterR = DiscordEmoji.FromUnicode("🇷");
            var letterW = DiscordEmoji.FromUnicode("🇼");
            var letterM = DiscordEmoji.FromUnicode("🇲");
            var letterE = DiscordEmoji.FromUnicode("🇪");

            DiscordMessage msg = null;
            string errorMsg = null;
            DiscordMessage txt;
            MessageReactionAddEventArgs emoji;

        step1:
            // step 1: define trigger string
            var embed = FormatFilter(filter, errorMsg, 1)
                .WithDescription(
                    "Any simple string that is used to flag potential content for a check using Validation regex.\n" +
                    "**Must** be sufficiently long to reduce the number of checks."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify a new **trigger**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, lastPage, nextPage, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == lastPage)
                {
                    if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                        goto step6;

                    if (filter.Actions.HasFlag(FilterAction.SendMessage))
                        goto step5;

                    goto step4;
                }
            }
            else if (txt?.Content != null)
            {
                var existing = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String.Equals(txt.Content, StringComparison.InvariantCultureIgnoreCase)).ConfigureAwait(false);
                if (existing != null)
                {
                    if (existing.Disabled)
                        db.Piracystring.Remove(existing);
                    else
                    {
                        errorMsg = $"Trigger `{txt.Content.Sanitize()}` already exists";
                        goto step1;
                    }
                }

                if (txt.Content.Length < Config.MinimumPiracyTriggerLength)
                {
                    errorMsg = "Trigger is too short";
                    goto step1;
                }

                filter.String = txt.Content;
            }
            else
                return (false, msg);

        step2:
            // step 2: context of the filter where it is applicable
            embed = FormatFilter(filter, errorMsg, 2)
                .WithDescription(
                    "Context of the filter indicates where it is applicable.\n" +
                    $"**`C`** = **`{FilterContext.Chat}`** will apply it in filtering discord messages.\n" +
                    $"**`L`** = **`{FilterContext.Log}`** will apply it during log parsing.\n" +
                    "Reactions will toggle the context, text message will set the specified flags."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify filter **context(s)**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, nextPage, letterC, letterL, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                    goto step1;

                if (emoji.Emoji == letterC)
                {
                    filter.Context ^= FilterContext.Chat;
                    goto step2;
                }

                if (emoji.Emoji == letterL)
                {
                    filter.Context ^= FilterContext.Log;
                    goto step2;
                }
            }
            else if (txt != null)
            {
                var flagsTxt = txt.Content.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                FilterContext newCtx = 0;
                foreach (var f in flagsTxt)
                {
                    switch (f.ToUpperInvariant())
                    {
                        case "C":
                        case "CHAT":
                            newCtx |= FilterContext.Chat;
                            break;
                        case "L":
                        case "LOG":
                        case "LOGS":
                            newCtx |= FilterContext.Log;
                            break;
                        case "ABORT":
                            return (false, msg);
                        case "-":
                        case "SKIP":
                        case "NEXT":
                            break;
                        default:
                            errorMsg = $"Unknown context `{f}`.";
                            goto step2;
                    }
                }
                filter.Context = newCtx;
            }
            else
                return (false, msg);

        step3:
            // step 3: actions that should be performed on match
            embed = FormatFilter(filter, errorMsg, 3)
                .WithDescription(
                    "Actions that will be executed on positive match.\n" +
                    $"**`R`** = **`{FilterAction.RemoveContent}`** will remove the message / log.\n" +
                    $"**`W`** = **`{FilterAction.IssueWarning}`** will issue a warning to the user.\n" +
                    $"**`M`** = **`{FilterAction.SendMessage}`** send _a_ message with an explanation of why it was removed.\n" +
                    $"**`E`** = **`{FilterAction.ShowExplain}`** show `explain` for the specified term (**not implemented**).\n" +
                    "Reactions will toggle the action, text message will set the specified flags."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify filter **action(s)**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, nextPage, letterR, letterW, letterM, letterE, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                    goto step2;

                if (emoji.Emoji == letterR)
                {
                    filter.Actions ^= FilterAction.RemoveContent;
                    goto step3;
                }

                if (emoji.Emoji == letterW)
                {
                    filter.Actions ^= FilterAction.IssueWarning;
                    goto step3;
                }

                if (emoji.Emoji == letterM)
                {
                    filter.Actions ^= FilterAction.SendMessage;
                    goto step3;
                }

                if (emoji.Emoji == letterE)
                {
                    filter.Actions ^= FilterAction.ShowExplain;
                    goto step3;
                }
            }
            else if (txt != null)
            {
                var flagsTxt = txt.Content.ToUpperInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                if (flagsTxt.Length == 1
                    && flagsTxt[0].Length <= Enum.GetValues(typeof(FilterAction)).Length)
                    flagsTxt = flagsTxt[0].Select(c => c.ToString()).ToArray();
                FilterAction newActions = 0;
                foreach (var f in flagsTxt)
                {
                    switch (f)
                    {
                        case "R":
                        case "REMOVE":
                        case "REMOVEMESSAGE":
                            newActions |= FilterAction.RemoveContent;
                            break;
                        case "W":
                        case "WARN":
                        case "WARNING":
                        case "ISSUEWARNING":
                            newActions |= FilterAction.IssueWarning;
                            break;
                        case "M":
                        case "MSG":
                        case "MESSAGE":
                        case "SENDMESSAGE":
                            newActions |= FilterAction.SendMessage;
                            break;
                        case "E":
                        case "X":
                        case "EXPLAIN":
                        case "SHOWEXPLAIN":
                        case "SENDEXPLAIN":
                            newActions |= FilterAction.ShowExplain;
                            break;
                        case "ABORT":
                            return (false, msg);
                        case "-":
                        case "SKIP":
                        case "NEXT":
                            break;
                        default:
                            errorMsg = $"Unknown action `{f.ToLowerInvariant()}`.";
                            goto step2;
                    }
                }
                filter.Actions = newActions;
            }
            else
                return (false, msg);

        step4:
            // step 4: validation regex to filter out false positives of the plaintext triggers
            embed = FormatFilter(filter, errorMsg, 4)
                .WithDescription(
                    "Validation [regex](https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference) to optionally perform more strict trigger check.\n" +
                    "**Please [test](https://regex101.com/) your regex**. Following flags are enabled: Multiline, IgnoreCase.\n" +
                    "Additional validation can help reduce false positives of a plaintext trigger match."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify filter **validation regex**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            var next = (filter.Actions & (FilterAction.SendMessage | FilterAction.ShowExplain)) == 0 ? firstPage : nextPage;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, next, (string.IsNullOrEmpty(filter.ValidatingRegex) ? null : trash), (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                    goto step3;

                if (emoji.Emoji == firstPage)
                    goto step1;

                if (emoji.Emoji == trash)
                    filter.ValidatingRegex = null;
            }
            else if (txt != null)
            {
                if (string.IsNullOrWhiteSpace(txt.Content) || txt.Content == "-" || txt.Content == ".*")
                    filter.ValidatingRegex = null;
                else
                {
                    try
                    {
                        Regex.IsMatch("test", txt.Content, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    }
                    catch (Exception e)
                    {
                        errorMsg = "Invalid regex expression: " + e.Message;
                        goto step4;
                    }

                    filter.ValidatingRegex = txt.Content;
                }
            }
            else
                return (false, msg);

            if (filter.Actions.HasFlag(FilterAction.SendMessage))
                goto step5;
            else if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                goto step6;
            else
                goto stepConfirm;

        step5:
            // step 5: optional custom message for the user
            embed = FormatFilter(filter, errorMsg, 5)
                .WithDescription(
                    "Optional custom message sent to the user.\n" +
                    "If left empty, default piracy warning message will be used."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify filter **validation regex**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            next = (filter.Actions.HasFlag(FilterAction.ShowExplain) ? nextPage : firstPage);
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, next, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                    goto step4;

                if (emoji.Emoji == firstPage)
                    goto step1;
            }
            else if (txt != null)
            {
                if (string.IsNullOrWhiteSpace(txt.Content) || txt.Content == "-")
                    filter.CustomMessage = null;
                else
                    filter.CustomMessage = txt.Content;
            }
            else
                return (false, msg);

            if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                goto step6;
            else
                goto stepConfirm;

        step6:
            // step 6: show explanation for the term
            embed = FormatFilter(filter, errorMsg, 6)
                .WithDescription(
                    "Explanation term that is used to show an explanation.\n" +
                    "**__Currently not implemented__**."
                );
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Please specify filter **explanation term**", embed: embed).ConfigureAwait(false);
            errorMsg = null;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, firstPage, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                {
                    if (filter.Actions.HasFlag(FilterAction.SendMessage))
                        goto step5;
                    else
                        goto step4;
                }

                if (emoji.Emoji == firstPage)
                    goto step1;
            }
            else if (txt != null)
            {
                if (string.IsNullOrWhiteSpace(txt.Content) || txt.Content == "-")
                    filter.ExplainTerm = null;
                else
                {
                    var existingTerm = await db.Explanation.FirstOrDefaultAsync(exp => exp.Keyword == txt.Content.ToLowerInvariant()).ConfigureAwait(false);
                    if (existingTerm == null)
                    {
                        errorMsg = $"Term `{txt.Content.ToLowerInvariant().Sanitize()}` is not defined.";
                        goto step6;
                    }

                    filter.ExplainTerm = txt.Content;
                }
            }
            else
                return (false, msg);

        stepConfirm:
            // last step: confirm
            if (errorMsg == null && !filter.IsComplete())
                errorMsg = "Some required properties are not defined";
            embed = FormatFilter(filter, errorMsg);
            msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Does this look good? (y/n)", embed: embed.Build()).ConfigureAwait(false);
            errorMsg = null;
            (msg, txt, emoji) = await interact.WaitForMessageOrReactionAsync(msg, ctx.User, InteractTimeout, abort, previousPage, firstPage, (filter.IsComplete() ? saveEdit : null)).ConfigureAwait(false);
            if (emoji != null)
            {
                if (emoji.Emoji == abort)
                    return (false, msg);

                if (emoji.Emoji == saveEdit)
                    return (true, msg);

                if (emoji.Emoji == previousPage)
                {
                    if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                        goto step6;

                    if (filter.Actions.HasFlag(FilterAction.SendMessage))
                        goto step5;

                    goto step4;
                }

                if (emoji.Emoji == firstPage)
                    goto step1;
            }
            else if (!string.IsNullOrEmpty(txt?.Content))
            {
                if (!filter.IsComplete())
                    goto step5;

                switch (txt.Content.ToLowerInvariant())
                {
                    case "yes":
                    case "y":
                    case "✅":
                    case "☑":
                    case "✔":
                    case "👌":
                    case "👍":
                        return (true, msg);
                    case "no":
                    case "n":
                    case "❎":
                    case "❌":
                    case "👎":
                        return (false, msg);
                    default:
                        errorMsg = "I don't know what you mean, so I'll just abort";
                        if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                            goto step6;

                        if (filter.Actions.HasFlag(FilterAction.SendMessage))
                            goto step5;

                        goto step4;
                }
            }
            else
            {
                return (false, msg);
            }
            return (false, msg);
        }

        private static DiscordEmbedBuilder FormatFilter(Piracystring filter, string error = null, int highlight = -1)
        {
            var field = 1;
            var result = new DiscordEmbedBuilder
            {
                Title = "Filter preview",
                Color = string.IsNullOrEmpty(error) ? Config.Colors.Help : Config.Colors.Maintenance,
            };
            if (!string.IsNullOrEmpty(error))
                result.AddField("Entry error", error);

            var validTrigger = string.IsNullOrEmpty(filter.String) || filter.String.Length < Config.MinimumPiracyTriggerLength ? "⚠ " : "";
            result.AddFieldEx(validTrigger + "Trigger", filter.String, highlight == field++, true)
                .AddFieldEx("Context", filter.Context.ToString(), highlight == field++, true)
                .AddFieldEx("Actions", filter.Actions.ToFlagsString(), highlight == field++, true)
                .AddFieldEx("Validation", filter.ValidatingRegex, highlight == field++, true);
            if (filter.Actions.HasFlag(FilterAction.SendMessage))
                result.AddFieldEx("Message", filter.CustomMessage, highlight == field, true);
            field++;
            if (filter.Actions.HasFlag(FilterAction.ShowExplain))
            {
                var validExplainTerm = string.IsNullOrEmpty(filter.ExplainTerm) ? "⚠ " : "";
                result.AddFieldEx(validExplainTerm + "Explain", filter.ExplainTerm, highlight == field, true);
            }
#if DEBUG
            result.WithFooter("Test bot instance");
#endif
            return result;
        }
    }
}
