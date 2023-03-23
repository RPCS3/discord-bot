using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands.Attributes;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Interactivity.Extensions;
using Microsoft.EntityFrameworkCore;
using Exception = System.Exception;

namespace CompatBot.Commands;

[Group("filters"), Aliases("piracy", "filter"), RequiresBotSudoerRole, RequiresDm]
[Description("Used to manage content filters. **Works only in DM**")]
internal sealed class ContentFilters: BaseCommandModuleCustom
{
    private static readonly TimeSpan InteractTimeout = TimeSpan.FromMinutes(5);
    private static readonly char[] Separators = {' ', ',', ';', '|'};
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    [Command("list")]
    [Description("Lists all filters")]
    public async Task List(CommandContext ctx)
    {
        var table = new AsciiTable(
            new AsciiColumn("ID", alignToRight: true),
            new AsciiColumn("Trigger"),
            new AsciiColumn("Validation", maxWidth: 2048),
            new AsciiColumn("Context", maxWidth: 4096),
            new AsciiColumn("Actions"),
            new AsciiColumn("Custom message", maxWidth: 2048)
        );
        await using var db = new BotDb();
        var duplicates = new Dictionary<string, FilterContext>(StringComparer.InvariantCultureIgnoreCase);
        var filters = db.Piracystring.Where(ps => !ps.Disabled).AsNoTracking().AsEnumerable().OrderBy(ps => ps.String.ToUpperInvariant()).ToList();
        var nonUniqueTriggers = (
            from f in filters
            group f by f.String.ToUpperInvariant()
            into g
            where g.Count() > 1
            select g.Key
        ).ToList();
        foreach (var t in nonUniqueTriggers)
        {
            var duplicateFilters = filters.Where(ps => ps.String.Equals(t, StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (FilterContext fctx in Enum.GetValues(typeof(FilterContext)))
            {
                if (duplicateFilters.Count(f => (f.Context & fctx) == fctx) > 1)
                {
                    if (duplicates.TryGetValue(t, out var fctxDup))
                        duplicates[t] = fctxDup | fctx;
                    else
                        duplicates[t] = fctx;
                }
            }
        }
        foreach (var item in filters)
        {
            var ctxl = item.Context.ToString();
            if (duplicates.Count > 0
                && duplicates.TryGetValue(item.String, out var fctx)
                && (item.Context & fctx) != 0)
                ctxl = "❗ " + ctxl;
            table.Add(
                item.Id.ToString(),
                item.String.Sanitize(),
                item.ValidatingRegex ?? "",
                ctxl,
                item.Actions.ToFlagsString(),
                item.CustomMessage ?? ""
            );
        }
        var result = new StringBuilder(table.ToString(false)).AppendLine()
            .AppendLine(FilterActionExtensions.GetLegend(""));
        await using var output = Config.MemoryStreamManager.GetStream();
        //await using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
        await using (var writer = new StreamWriter(output, leaveOpen: true))
            await writer.WriteAsync(result.ToString()).ConfigureAwait(false);
        output.Seek(0, SeekOrigin.Begin);
        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().AddFile("filters.txt", output)).ConfigureAwait(false);
    }

    [Command("add"), Aliases("create")]
    [Description("Adds a new content filter")]
    public async Task Add(CommandContext ctx, [RemainingText, Description("A plain string to match")] string trigger)
    {
        await using var db = new BotDb();
        Piracystring? filter;
        if (string.IsNullOrEmpty(trigger))
            filter = new Piracystring();
        else
        {
            filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String == trigger && ps.Disabled).ConfigureAwait(false);
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
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatFilter(filter).WithTitle("Created a new content filter #" + filter.Id)).ConfigureAwait(false);
            var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
            var reportMsg = $"{member?.GetMentionWithNickname()} added a new content filter: `{filter.String.Sanitize()}`";
            if (!string.IsNullOrEmpty(filter.ValidatingRegex))
                reportMsg += $"\nValidation: `{filter.ValidatingRegex}`";
            await ctx.Client.ReportAsync("🆕 Content filter created", reportMsg, null, ReportSeverity.Low).ConfigureAwait(false);
            ContentFilter.RebuildMatcher();
        }
        else
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Content filter creation aborted").ConfigureAwait(false);
    }

    [Command("import"), RequiresBotSudoerRole]
    [Description("Import suspicious strings for a certain dump collection from attached dat file (zip is fine)")]
    public async Task Import(CommandContext ctx)
    {
        if (ctx.Message.Attachments.Count == 0)
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "No attached DAT file", true).ConfigureAwait(false);
            return;
        }

        if (!await ImportLock.WaitAsync(0))
        {
            await ctx.ReactWithAsync(Config.Reactions.Failure, "Another import is in progress", true).ConfigureAwait(false);
            return;
        }
        var count = 0;
        try
        {
            var attachment = ctx.Message.Attachments[0];
            await using var datStream = Config.MemoryStreamManager.GetStream();
            using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
            await using var attachmentStream = await httpClient.GetStreamAsync(attachment.Url, Config.Cts.Token).ConfigureAwait(false);
            if (attachment.FileName.ToLower().EndsWith(".dat"))
                await attachmentStream.CopyToAsync(datStream, Config.Cts.Token).ConfigureAwait(false);
            else if (attachment.FileName.ToLower().EndsWith(".zip"))
            {
                using var zipStream = new ZipArchive(attachmentStream, ZipArchiveMode.Read);
                var entry = zipStream.Entries.FirstOrDefault(e => e.Name.ToLower().EndsWith(".dat"));
                if (entry is null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Attached ZIP file doesn't contain DAT file", true).ConfigureAwait(false);
                    return;
                }

                await using var entryStream = entry.Open();
                await entryStream.CopyToAsync(datStream, Config.Cts.Token).ConfigureAwait(false);
            }
            else
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Attached file is not recognized", true).ConfigureAwait(false);
                return;
            }

            datStream.Seek(0, SeekOrigin.Begin);
            try
            {
                var xml = await XDocument.LoadAsync(datStream, LoadOptions.None, Config.Cts.Token).ConfigureAwait(false);
                if (xml.Root is null)
                {
                    await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to read DAT file as XML", true).ConfigureAwait(false);
                    return;
                }

                await using var db = new BotDb();
                foreach (var element in xml.Root.Elements("game"))
                {
                    var name = element.Element("rom")?.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    // only match for "complex" names with several regions, or region-languages, or explicit revision
                    if (!Regex.IsMatch(name, @" (\(.+\)\s*\(.+\)|\(\w+(,\s*\w+)+\))\.iso$"))
                        continue;

                    name = name[..^4]; //-.iso
                    if (await db.SuspiciousString.AnyAsync(ss => ss.String == name).ConfigureAwait(false))
                        continue;

                    db.SuspiciousString.Add(new() {String = name});
                    count++;
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, $"Failed to load DAT file {attachment.FileName}");
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Failed to read DAT file: " + e.Message, true).ConfigureAwait(false);
                return;
            }

            await ctx.ReactWithAsync(Config.Reactions.Success, $"Successfully imported {count} item{(count == 1 ? "" : "s")}", true).ConfigureAwait(false);
            ContentFilter.RebuildMatcher();
        }
        finally
        {
            ImportLock.Release();
        }
    }
        
    [Command("edit"), Aliases("fix", "update", "change")]
    [Description("Modifies the specified content filter")]
    public async Task Edit(CommandContext ctx, [Description("Filter ID")] int id)
    {
        await using var db = new BotDb();
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id && !ps.Disabled).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.Channel.SendMessageAsync("Specified filter does not exist").ConfigureAwait(false);
            return;
        }

        await EditFilterCmd(ctx, db, filter).ConfigureAwait(false);
    }

    [Command("edit")]
    public async Task Edit(CommandContext ctx, [Description("Trigger to edit"), RemainingText] string trigger)
    {
        await using var db = new BotDb();
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String == trigger && !ps.Disabled).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.Channel.SendMessageAsync("Specified filter does not exist").ConfigureAwait(false);
            return;
        }

        await EditFilterCmd(ctx, db, filter).ConfigureAwait(false);
    }
        
    [Command("view"), Aliases("show")]
    [Description("Shows the details of the specified content filter")]
    public async Task View(CommandContext ctx, [Description("Filter ID")] int id)
    {
        await using var db = new BotDb();
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id && !ps.Disabled).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.Channel.SendMessageAsync("Specified filter does not exist").ConfigureAwait(false);
            return;
        }

        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().WithEmbed(FormatFilter(filter))).ConfigureAwait(false);
    }
        
    [Command("view")]
    [Description("Shows the details of the specified content filter")]
    public async Task View(CommandContext ctx, [Description("Trigger to view"), RemainingText] string trigger)
    {
        await using var db = new BotDb();
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String == trigger && !ps.Disabled).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.Channel.SendMessageAsync("Specified filter does not exist").ConfigureAwait(false);
            return;
        }

        await ctx.Channel.SendMessageAsync(new DiscordMessageBuilder().WithEmbed(FormatFilter(filter))).ConfigureAwait(false);
    }

    [Command("remove"), Aliases("delete", "del")]
    [Description("Removes a content filter trigger")]
    public async Task Remove(CommandContext ctx, [Description("Filter IDs to remove, separated with spaces")] params int[] ids)
    {
        int removedFilters;
        var removedTriggers = new StringBuilder();
        await using (var db = new BotDb())
        {
            foreach (var f in db.Piracystring.Where(ps => ids.Contains(ps.Id) && !ps.Disabled))
            {
                f.Disabled = true;
                removedTriggers.Append($"\n`{f.String.Sanitize()}`");
            }
            removedFilters = await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }

        if (removedFilters < ids.Length)
            await ctx.Channel.SendMessageAsync("Some ids couldn't be removed.").ConfigureAwait(false);
        else
        {
            await ctx.ReactWithAsync(Config.Reactions.Success, $"Trigger{StringUtils.GetSuffix(ids.Length)} successfully removed!").ConfigureAwait(false);
            var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
            var s = removedFilters == 1 ? "" : "s";
            var filterList = removedTriggers.ToString();
            if (removedFilters == 1)
                filterList = filterList.TrimStart();
            await ctx.Client.ReportAsync($"📴 Content filter{s} removed", $"{member?.GetMentionWithNickname()} removed {removedFilters} content filter{s}: {filterList}".Trim(EmbedPager.MaxDescriptionLength), null, ReportSeverity.Medium).ConfigureAwait(false);
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

        await using (var db = new BotDb())
        {
            var f = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String.Equals(trigger, StringComparison.InvariantCultureIgnoreCase) && !ps.Disabled).ConfigureAwait(false);
            if (f is null)
            {
                await ctx.ReactWithAsync(Config.Reactions.Failure, "Specified filter does not exist").ConfigureAwait(false);
                return;
            }

            f.Disabled = true;
            await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }

        await ctx.ReactWithAsync(Config.Reactions.Success, "Trigger was removed").ConfigureAwait(false);
        var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
        await ctx.Client.ReportAsync("📴 Content filter removed", $"{member?.GetMentionWithNickname()} removed 1 content filter: `{trigger.Sanitize()}`", null, ReportSeverity.Medium).ConfigureAwait(false);
        ContentFilter.RebuildMatcher();
    }

    private static async Task EditFilterCmd(CommandContext ctx, BotDb db, Piracystring filter)
    {
        var (success, msg) = await EditFilterPropertiesAsync(ctx, db, filter).ConfigureAwait(false);
        if (success)
        {
            await db.SaveChangesAsync().ConfigureAwait(false);
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, embed: FormatFilter(filter).WithTitle("Updated content filter")).ConfigureAwait(false);
            var member = ctx.Member ?? ctx.Client.GetMember(ctx.User);
            var reportMsg = $"{member?.GetMentionWithNickname()} changed content filter #{filter.Id} (`{filter.Actions.ToFlagsString()}`): `{filter.String.Sanitize()}`";
            if (!string.IsNullOrEmpty(filter.ValidatingRegex))
                reportMsg += $"\nValidation: `{filter.ValidatingRegex}`";
            await ctx.Client.ReportAsync("🆙 Content filter updated", reportMsg, null, ReportSeverity.Low).ConfigureAwait(false);
            ContentFilter.RebuildMatcher();
        }
        else
            await msg.UpdateOrCreateMessageAsync(ctx.Channel, "Content filter update aborted").ConfigureAwait(false);
    }

    private static async Task<(bool success, DiscordMessage? message)> EditFilterPropertiesAsync(CommandContext ctx, BotDb db, Piracystring filter)
    {
        try
        {
            return await EditFilterPropertiesInternalAsync(ctx, db, filter).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to edit content filter");
            return (false, null);
        }
    }
    private static async Task<(bool success, DiscordMessage? message)> EditFilterPropertiesInternalAsync(CommandContext ctx, BotDb db, Piracystring filter)
    {
        var interact = ctx.Client.GetInteractivity();
        var abort = new DiscordButtonComponent(ButtonStyle.Danger, "filter:edit:abort", "Cancel", emoji: new(DiscordEmoji.FromUnicode("✖")));
        var lastPage = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:last", "To Last Field", emoji: new(DiscordEmoji.FromUnicode("⏭")));
        var firstPage = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:first", "To First Field", emoji: new(DiscordEmoji.FromUnicode("⏮")));
        var previousPage = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:previous", "Previous", emoji: new(DiscordEmoji.FromUnicode("◀")));
        var nextPage = new DiscordButtonComponent(ButtonStyle.Primary, "filter:edit:next", "Next", emoji: new(DiscordEmoji.FromUnicode("▶")));
        var trash = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:trash", "Clear", emoji: new(DiscordEmoji.FromUnicode("🗑")));
        var saveEdit = new DiscordButtonComponent(ButtonStyle.Success, "filter:edit:save", "Save", emoji: new(DiscordEmoji.FromUnicode("💾")));

        var contextChat = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:context:chat", "Chat");
        var contextLog = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:context:log", "Log");
        var actionR = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:r", "R");
        var actionW = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:w", "W");
        var actionM = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:m", "M");
        var actionE = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:e", "E");
        var actionU = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:u", "U");
        var actionK = new DiscordButtonComponent(ButtonStyle.Secondary, "filter:edit:action:k", "K");

        var minus = new DiscordComponentEmoji(DiscordEmoji.FromUnicode("➖"));
        var plus = new DiscordComponentEmoji(DiscordEmoji.FromUnicode("➕"));

        DiscordMessage? msg = null;
        string? errorMsg = null;
        DiscordMessage? txt;
        ComponentInteractionCreateEventArgs? btn;

        step1:
        // step 1: define trigger string
        var embed = FormatFilter(filter, errorMsg, 1)
            .WithDescription(
                "Any simple string that is used to flag potential content for a check using Validation regex.\n" +
                "**Must** be sufficiently long to reduce the number of checks."
            );
        saveEdit.SetEnabled(filter.IsComplete());
        var messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify a new **trigger**")
            .WithEmbed(embed)
            .AddComponents(lastPage, nextPage, saveEdit, abort);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == lastPage.CustomId)
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
        saveEdit.SetEnabled(filter.IsComplete());
        contextChat.SetEmoji(filter.Context.HasFlag(FilterContext.Chat) ? minus : plus);
        contextLog.SetEmoji(filter.Context.HasFlag(FilterContext.Log) ? minus : plus);
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify filter **context(s)**")
            .WithEmbed(embed)
            .AddComponents(previousPage, nextPage, saveEdit, abort)
            .AddComponents(contextChat, contextLog);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
                goto step1;

            if (btn.Id == contextChat.CustomId)
            {
                filter.Context ^= FilterContext.Chat;
                goto step2;
            }

            if (btn.Id == contextLog.CustomId)
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
                $"**`U`** = **`{FilterAction.MuteModQueue}`** mute mod queue reporting for this action.\n" +
                $"**`K`** = **`{FilterAction.Kick}`** kick user from server.\n" +
                "Buttons will toggle the action, text message will set the specified flags."
            );
        actionR.SetEmoji(filter.Actions.HasFlag(FilterAction.RemoveContent) ? minus : plus);
        actionW.SetEmoji(filter.Actions.HasFlag(FilterAction.IssueWarning) ? minus : plus);
        actionM.SetEmoji(filter.Actions.HasFlag(FilterAction.SendMessage) ? minus : plus);
        actionE.SetEmoji(filter.Actions.HasFlag(FilterAction.ShowExplain) ? minus : plus);
        actionU.SetEmoji(filter.Actions.HasFlag(FilterAction.MuteModQueue) ? minus : plus);
        actionK.SetEmoji(filter.Actions.HasFlag(FilterAction.Kick) ? minus : plus);
        saveEdit.SetEnabled(filter.IsComplete());
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify filter **action(s)**")
            .WithEmbed(embed)
            .AddComponents(previousPage, nextPage, saveEdit, abort)
            .AddComponents(actionR, actionW, actionM, actionE, actionU)
            .AddComponents(actionK);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
                goto step2;

            if (btn.Id == actionR.CustomId)
            {
                filter.Actions ^= FilterAction.RemoveContent;
                goto step3;
            }

            if (btn.Id == actionW.CustomId)
            {
                filter.Actions ^= FilterAction.IssueWarning;
                goto step3;
            }

            if (btn.Id == actionM.CustomId)
            {
                filter.Actions ^= FilterAction.SendMessage;
                goto step3;
            }

            if (btn.Id == actionE.CustomId)
            {
                filter.Actions ^= FilterAction.ShowExplain;
                goto step3;
            }

            if (btn.Id == actionU.CustomId)
            {
                filter.Actions ^= FilterAction.MuteModQueue;
                goto step3;
            }

            if (btn.Id == actionK.CustomId)
            {
                filter.Actions ^= FilterAction.Kick;
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
                    case "U":
                    case "MMQ":
                    case "MUTE":
                    case "MUTEMODQUEUE":
                        newActions |= FilterAction.MuteModQueue;
                        break;
                    case "K":
                    case "KICK":
                        newActions |= FilterAction.Kick;
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
        var next = (filter.Actions & (FilterAction.SendMessage | FilterAction.ShowExplain)) == 0 ? firstPage : nextPage;
        trash.SetDisabled(string.IsNullOrEmpty(filter.ValidatingRegex));
        saveEdit.SetEnabled(filter.IsComplete());
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify filter **validation regex**")
            .WithEmbed(embed)
            .AddComponents(previousPage, next, trash, saveEdit, abort);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
                goto step3;

            if (btn.Id == firstPage.CustomId)
                goto step1;

            if (btn.Id == trash.CustomId)
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
                    _ = Regex.IsMatch("test", txt.Content, RegexOptions.Multiline | RegexOptions.IgnoreCase);
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
        next = (filter.Actions.HasFlag(FilterAction.ShowExplain) ? nextPage : firstPage);
        saveEdit.SetEnabled(filter.IsComplete());
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify filter **validation regex**")
            .WithEmbed(embed)
            .AddComponents(previousPage, next, saveEdit, abort);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
                goto step4;

            if (btn.Id == firstPage.CustomId)
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
        saveEdit.SetEnabled(filter.IsComplete());
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Please specify filter **explanation term**")
            .WithEmbed(embed)
            .AddComponents(previousPage, firstPage, saveEdit, abort);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
            {
                if (filter.Actions.HasFlag(FilterAction.SendMessage))
                    goto step5;
                else
                    goto step4;
            }

            if (btn.Id == firstPage.CustomId)
                goto step1;
        }
        else if (txt != null)
        {
            if (string.IsNullOrWhiteSpace(txt.Content) || txt.Content == "-")
                filter.ExplainTerm = null;
            else
            {
                var term = txt.Content.ToLowerInvariant();
                var existingTerm = await db.Explanation.FirstOrDefaultAsync(exp => exp.Keyword == term).ConfigureAwait(false);
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
        saveEdit.SetEnabled(filter.IsComplete());
        messageBuilder = new DiscordMessageBuilder()
            .WithContent("Does this look good? (y/n)")
            .WithEmbed(FormatFilter(filter, errorMsg))
            .AddComponents(previousPage, firstPage, saveEdit, abort);
        errorMsg = null;
        msg = await msg.UpdateOrCreateMessageAsync(ctx.Channel, messageBuilder).ConfigureAwait(false);
        (txt, btn) = await interact.WaitForMessageOrButtonAsync(msg, ctx.User, InteractTimeout).ConfigureAwait(false);
        if (btn != null)
        {
            if (btn.Id == abort.CustomId)
                return (false, msg);

            if (btn.Id == saveEdit.CustomId)
                return (true, msg);

            if (btn.Id == previousPage.CustomId)
            {
                if (filter.Actions.HasFlag(FilterAction.ShowExplain))
                    goto step6;

                if (filter.Actions.HasFlag(FilterAction.SendMessage))
                    goto step5;

                goto step4;
            }

            if (btn.Id == firstPage.CustomId)
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

    private static DiscordEmbedBuilder FormatFilter(Piracystring filter, string? error = null, int highlight = -1)
    {
        var field = 1;
        var result = new DiscordEmbedBuilder
        {
            Title = "Filter preview",
            Color = string.IsNullOrEmpty(error) ? Config.Colors.Help : Config.Colors.Maintenance,
        };
        if (!string.IsNullOrEmpty(error))
            result.AddField("Entry error", error);

        var validTrigger = string.IsNullOrEmpty(filter.String) || filter.String.Length < Config.MinimumPiracyTriggerLength ? "⚠️ " : "";
        result.AddFieldEx(validTrigger + "Trigger", filter.String, highlight == field++, true)
            .AddFieldEx("Context", filter.Context.ToString(), highlight == field++, true)
            .AddFieldEx("Actions", filter.Actions.ToFlagsString(), highlight == field++, true)
            .AddFieldEx("Validation", filter.ValidatingRegex?.Trim(EmbedPager.MaxFieldLength) ?? "", highlight == field++, true);
        if (filter.Actions.HasFlag(FilterAction.SendMessage))
            result.AddFieldEx("Message", filter.CustomMessage?.Trim(EmbedPager.MaxFieldLength) ?? "", highlight == field, true);
        field++;
        if (filter.Actions.HasFlag(FilterAction.ShowExplain))
        {
            var validExplainTerm = string.IsNullOrEmpty(filter.ExplainTerm) ? "⚠️ " : "";
            result.AddFieldEx(validExplainTerm + "Explain", filter.ExplainTerm ?? "", highlight == field, true);
        }
#if DEBUG
        result.WithFooter("Test bot instance");
#endif
        return result;
    }
}