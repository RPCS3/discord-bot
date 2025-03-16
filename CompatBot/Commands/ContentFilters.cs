using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Commands.ChoiceProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.SlashCommands.ArgumentModifiers;
using DSharpPlus.Interactivity;
using Microsoft.EntityFrameworkCore;
using org.mariuszgromada.math.mxparser.parsertokens;
using Exception = System.Exception;

namespace CompatBot.Commands;

[Command("filter"), RequiresBotSudoerRole]
[Description("Used to manage content filters. **Works only in DM**")]
internal sealed partial class ContentFilters
{
    private static readonly TimeSpan InteractTimeout = TimeSpan.FromMinutes(5);
    private static readonly char[] Separators = [' ', ',', ';', '|'];
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    // match for "complex" names with several regions, or region-languages, or explicit revision
    [GeneratedRegex(@" (\(.+\)\s*\(.+\)|\(\w+(,\s*\w+)+\))\.iso$")]
    private static partial Regex ExtraIsoInfoPattern();

    [Command("dump")]
    [Description("Saves all filters as a text file attachment")]
    public static async ValueTask List(SlashCommandContext ctx)
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
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
            foreach (FilterContext fctx in FilterActionExtensions.ActionFlagValues)
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
        var builder = new DiscordInteractionResponseBuilder().AddFile("filters.txt", output);
        if (ephemeral)
            builder = builder.AsEphemeral();
        await ctx.RespondAsync(builder).ConfigureAwait(false);
    }

    [Command("add")]
    [Description("Adds a new content filter")]
    public static async ValueTask Add(
        SlashCommandContext ctx,
        [Description("A plain string to match"), MinMaxLength(minLength: 3)]
        string trigger,
        //[Description("Context where filter is active (default is Chat and Logs)"), VariadicArgument(2, 0)]IReadOnlyList<FilterContext> context, // todo: use this when variadic bugs are fixed
        [Description("Context where filter is active (default is Chat and Logs)"), SlashChoiceProvider<FilterContextChoiceProvider>]
        int context = 0,
        //[Description("Actions performed by the filter (default is Remove, and Warn with Message)"), VariadicArgument(6, 0)]IReadOnlyList<FilterAction> action,
        [Description("Actions performed by the filter (default is Remove, and Warn with Message)"), SlashAutoCompleteProvider<FilterActionAutoCompleteProvider>]
        int action = 0,
        [Description("Validation regex (use https://regex101.com to test)")]
        string? validation = null,
        [Description("Custom message to send if `M`essage action was enabled (default is the piracy warning)")]
        string? message = null,
        [Description("Explanation to send if `E`xplain action was enabled"), SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string? explanation = null 
    )
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        if (validation is { Length: > 0 })
        {
            try
            {
                _ = Regex.IsMatch(
                    trigger,
                    validation, 
                    RegexOptions.Multiline | RegexOptions.IgnoreCase, 
                    TimeSpan.FromMilliseconds(100)
                );
            }
            catch (Exception e)
            {
                await ctx.RespondAsync($"❌ Invalid regex expression: {e.Message}", ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }
        }

        explanation = explanation?.ToLowerInvariant();
        await using var db = new BotDb();
        if (explanation is {Length: >0} && !await db.Explanation.AnyAsync(e => e.Keyword == explanation).ConfigureAwait(false))
        {
            await ctx.RespondAsync($"❌ Unknown explanation term: {explanation}", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }
        
        var isNewFilter = true;
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.String == trigger && ps.Disabled).ConfigureAwait(false);
        if (filter is null)
            filter = new() { String = trigger };
        else
        {
            filter.Disabled = false;
            isNewFilter = false;
        }
        if (isNewFilter)
        {
            filter.Context = FilterContext.Chat | FilterContext.Log;
            filter.Actions = FilterAction.RemoveContent | FilterAction.IssueWarning | FilterAction.SendMessage;
        }
        filter.ValidatingRegex = validation;
        if (context is not 0)
            filter.Context = (FilterContext)context;
        if (action is not 0)
            filter.Actions = (FilterAction)action;
        if (message is {Length: >0})
            filter.CustomMessage = message;
        if (explanation is { Length: > 0 })
            filter.ExplainTerm = explanation;
        if (filter.Actions.HasFlag(FilterAction.ShowExplain)
            && filter.ExplainTerm is not { Length: > 0 })
        {
            await ctx.RespondAsync("❌ Explain action flag was enabled, but no valid explanation term was provided.", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        if (isNewFilter)
            await db.Piracystring.AddAsync(filter).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);
        var resultEmbed = FormatFilter(filter).WithTitle("Created a new content filter #" + filter.Id);
        await ctx.RespondAsync(resultEmbed, ephemeral: ephemeral).ConfigureAwait(false);

        var member = ctx.Member ?? await ctx.Client.GetMemberAsync(ctx.User).ConfigureAwait(false);
        var reportMsg = $"{member?.GetMentionWithNickname()} added a new content filter: `{filter.String.Sanitize()}`";
        if (!string.IsNullOrEmpty(filter.ValidatingRegex))
            reportMsg += $"\nValidation: `{filter.ValidatingRegex}`";
        await ctx.Client.ReportAsync("🆕 Content filter created", reportMsg, null, ReportSeverity.Low).ConfigureAwait(false);
        ContentFilter.RebuildMatcher();
    }

    /*
    [Command("import"), RequiresBotSudoerRole]
    [Description("Import suspicious strings for a certain dump collection from attached dat file (zip is fine)")]
    public static async ValueTask Import(CommandContext ctx)
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


                    if (!ExtraIsoInfoPattern().IsMatch(name))
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
    */

    [Command("update")]
    [Description("Modifies the specified content filter")]
    public async Task Edit(SlashCommandContext ctx,
        [Description("Filter ID"), SlashAutoCompleteProvider<ContentFilterAutoCompleteProvider>]
        int id,
        [Description("A plain string to match"), MinMaxLength(minLength: 3)]
        string? trigger = null,
        //[Description("Context where filter is active (default is Chat and Logs)"), VariadicArgument(2, 0)]IReadOnlyList<FilterContext> context, // todo: use this when variadic bugs are fixed
        [Description("Context where filter is active (default is Chat and Logs)"), SlashChoiceProvider<FilterContextChoiceProvider>]
        int context = 0,
        //[Description("Actions performed by the filter (default is Remove, and Warn with Message)"), VariadicArgument(6, 0)]IReadOnlyList<FilterAction> action,
        [Description("Actions performed by the filter (default is Remove, and Warn with Message)"), SlashAutoCompleteProvider<FilterActionAutoCompleteProvider>]
        int action = 0,
        [Description("Validation regex (use https://regex101.com to test)")]
        string? validation = null,
        [Description("Custom message to send if `M`essage action was enabled (default is the piracy warning)")]
        string? message = null,
        [Description("Explanation to send if `E`xplain action was enabled"), SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string? explanation = null 
    )
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        if (validation is { Length: > 0 })
        {
            try
            {
                _ = Regex.IsMatch(
                    "test",
                    validation, 
                    RegexOptions.Multiline | RegexOptions.IgnoreCase, 
                    TimeSpan.FromMilliseconds(100)
                );
            }
            catch (Exception e)
            {
                await ctx.RespondAsync($"❌ Invalid regex expression: {e.Message}", ephemeral: ephemeral).ConfigureAwait(false);
                return;
            }
        }

        await using var db = new BotDb();
        explanation = explanation?.ToLowerInvariant();
        if (explanation is {Length: >0} && !await db.Explanation.AnyAsync(e => e.Keyword == explanation).ConfigureAwait(false))
        {
            await ctx.RespondAsync($"❌ Unknown explanation term: {explanation}", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }
        
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.RespondAsync($"❌ Unknown filter  ID: {id}", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }
        
        filter.Disabled = false;
        if (trigger is { Length: > 0 })
            filter.String = trigger;
        if (validation is { Length: >0 })
            filter.ValidatingRegex = validation;
        if (context is not 0)
            filter.Context = (FilterContext)context;
        if (action is not 0)
            filter.Actions = (FilterAction)action;
        if (message is {Length: >0})
            filter.CustomMessage = message;
        if (explanation is { Length: > 0 })
            filter.ExplainTerm = explanation;
        if (filter.Actions.HasFlag(FilterAction.ShowExplain)
            && filter.ExplainTerm is not { Length: > 0 })
        {
            await ctx.RespondAsync("❌ Explain action flag was enabled, but no valid explanation term was provided.", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
        var resultEmbed = FormatFilter(filter).WithTitle("Created a new content filter #" + filter.Id);
        await ctx.RespondAsync(resultEmbed, ephemeral: ephemeral).ConfigureAwait(false);

        var member = ctx.Member ?? await ctx.Client.GetMemberAsync(ctx.User).ConfigureAwait(false);
        var reportMsg = $"{member?.GetMentionWithNickname()} updated content filter #{filter.Id}: `{filter.String.Sanitize()}`";
        if (!string.IsNullOrEmpty(filter.ValidatingRegex))
            reportMsg += $"\nValidation: `{filter.ValidatingRegex}`";
        await ctx.Client.ReportAsync("🆙 Content filter created", reportMsg, null, ReportSeverity.Low).ConfigureAwait(false);
        ContentFilter.RebuildMatcher();
    }

    [Command("view")]
    [Description("Show the details of the specified content filter")]
    public static async ValueTask ViewById(
        SlashCommandContext ctx,
        [Description("Filter ID"), SlashAutoCompleteProvider<ContentFilterAutoCompleteProvider>] int id
    )
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        await using var db = new BotDb();
        var filter = await db.Piracystring.FirstOrDefaultAsync(ps => ps.Id == id && !ps.Disabled).ConfigureAwait(false);
        if (filter is null)
        {
            await ctx.RespondAsync("❌ Specified filter does not exist", ephemeral: ephemeral).ConfigureAwait(false);
            return;
        }

        var messageBuilder = new DiscordInteractionResponseBuilder().AddEmbed(FormatFilter(filter));
        if (ephemeral)
            messageBuilder = messageBuilder.AsEphemeral();
        await ctx.RespondAsync(messageBuilder).ConfigureAwait(false);
    }

    [Command("remove")]
    [Description("Removes a content filter trigger")]
    public static async ValueTask Remove(
        SlashCommandContext ctx,
        [Description("Filter to remove"), SlashAutoCompleteProvider<ContentFilterAutoCompleteProvider>]int id
    )
    {
        var ephemeral = !ctx.Channel.IsPrivate;
        int removedFilters;
        var removedTriggers = new StringBuilder();
        await using (var db = new BotDb())
        {
            foreach (var f in db.Piracystring.Where(ps => ps.Id == id && !ps.Disabled))
            {
                f.Disabled = true;
                removedTriggers.Append($"\n`{f.String.Sanitize()}`");
            }
            removedFilters = await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
        }

        if (removedFilters is 0)
            await ctx.RespondAsync("Nothing was removed.", ephemeral: ephemeral).ConfigureAwait(false);
        else
        {
            await ctx.RespondAsync($"✅ Content filter was successfully removed", ephemeral: ephemeral).ConfigureAwait(false);
            
            var member = ctx.Member ?? await ctx.Client.GetMemberAsync(ctx.User).ConfigureAwait(false);
            var s = removedFilters == 1 ? "" : "s";
            var filterList = removedTriggers.ToString();
            if (removedFilters == 1)
                filterList = filterList.TrimStart();
            await ctx.Client.ReportAsync($"📴 Content filter{s} removed", $"{member?.GetMentionWithNickname()} removed {removedFilters} content filter{s}: {filterList}".Trim(EmbedPager.MaxDescriptionLength), null, ReportSeverity.Medium).ConfigureAwait(false);
        }
        ContentFilter.RebuildMatcher();
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