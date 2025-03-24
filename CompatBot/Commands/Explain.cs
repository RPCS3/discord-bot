using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using CompatApiClient.Compression;
using CompatApiClient.Utils;
using CompatBot.Commands.AutoCompleteProviders;
using CompatBot.Database;
using CompatBot.Database.Providers;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Commands;

[Command("explain")]
internal static class Explain
{
    private const string TermListTitle = "Defined terms";

    [Command("show")]
    [Description("Show explanation for specified term")]
    public static async ValueTask Show(
        SlashCommandContext ctx,
        [Description("Keyword or topic"), SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string term,
        [Description("User to ping with the explanation")]
        DiscordUser? to = null
    )
    {
        var ephemeral = !(ctx.Channel.IsSpamChannel() || (ModProvider.IsMod(ctx.User.Id) && to is not null));
        var canPing = ModProvider.IsMod(ctx.User.Id);
        term = term.ToLowerInvariant();
        var result = await LookupTerm(term).ConfigureAwait(false);
        if (result.explanation is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Unknown term `{term.Sanitize(replaceBackTicks: true)}`.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var explainMsg = new DiscordInteractionResponseBuilder();
        if (ephemeral)
            explainMsg.AsEphemeral();
        if (to is null)
            explainMsg.WithContent(result.explanation.Text);
        else
        {
            var mention = new UserMention(to.Id);
            explainMsg.WithContent(
                $"""
                 {to.Mention} please read the explanation for `{result.explanation.Keyword.Sanitize(replaceBackTicks: true)}`:
                 {result.explanation.Text}
                 """
            );
            if (canPing)
                explainMsg.AddMention(mention);
        }
        if (result.explanation.Attachment is not { Length: > 0 })
        {
            await ctx.RespondAsync(explainMsg).ConfigureAwait(false);
            return;
        }
        
        await using var memStream = Config.MemoryStreamManager.GetStream(result.explanation.Attachment);
        memStream.Seek(0, SeekOrigin.Begin);
        explainMsg.AddFile(result.explanation.AttachmentFilename!, memStream);
        await ctx.RespondAsync(explainMsg).ConfigureAwait(false);
    }

    [Command("add"), RequiresBotModRole]
    [Description("Add a new explanation")]
    public static async ValueTask Add(
        SlashCommandContext ctx,
        [Description("A term to explain")]
        string term,
        [Description("Explanation text")]
        string explanation,
        [Description("Explanation file attachment. Usually an image or a short video. Keep it as small as possible")]
        DiscordAttachment? attachment = null
    )
    {
        try
        {
            term = term.ToLowerInvariant().StripQuotes();
            byte[]? attachmentContent = null;
            string? attachmentFilename = null;
            if (attachment is {} att)
            {
                attachmentFilename = att.FileName;
                await ctx.DeferResponseAsync(true).ConfigureAwait(false);
                try
                {
                    using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                    attachmentContent = await httpClient.GetByteArrayAsync(att.Url).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Failed to download explanation attachment " + ctx);
                    await ctx.RespondAsync("Failed to download explanation attachment", ephemeral: true).ConfigureAwait(false);
                    return;
                }
            }

            if (string.IsNullOrEmpty(explanation) && string.IsNullOrEmpty(attachmentFilename))
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} An explanation for the term _or_ an attachment must be provided", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            await using var db = new BotDb();
            if (await db.Explanation.AnyAsync(e => e.Keyword == term).ConfigureAwait(false))
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} `{term}` is already defined", ephemeral: true).ConfigureAwait(false);
                return;
            }
            
            var entity = new Explanation
            {
                Keyword = term,
                Text = explanation,
                Attachment = attachmentContent,
                AttachmentFilename = attachmentFilename
            };
            await db.Explanation.AddAsync(entity).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await ctx.RespondAsync($"{Config.Reactions.Success} `{term}` was added", ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Error(e, $"Failed to add an explanation for `{term}`");
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to add an explanation", ephemeral: true).ConfigureAwait(false);
        }
    }

    [Command("update"), RequiresBotModRole]
    [Description("Update an explanation")]
    public static async ValueTask Update(
        SlashCommandContext ctx,
        [Description("A term to update"),  SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string term,
        [Description("Rename to new term")]
        string? renameTo = null,
        [Description("New explanation text")]
        string? explanation = null,
        [Description("Explanation file attachment. Usually an image or a short video. Keep it as small as possible")]
        DiscordAttachment? attachment = null
    )
    {
        term = term.ToLowerInvariant().StripQuotes();
        byte[]? attachmentContent = null;
        string? attachmentFilename = null;
        if (attachment is {} att)
        {
            await ctx.DeferResponseAsync(true).ConfigureAwait(false);
            attachmentFilename = att.FileName;
            try
            {
                using var httpClient = HttpClientFactory.Create(new CompressionMessageHandler());
                attachmentContent = await httpClient.GetByteArrayAsync(att.Url).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to download explanation attachment " + ctx);
                await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to download new attachment", ephemeral: true).ConfigureAwait(false);
                return;
            }
        }
        
        await using var db = new BotDb();
        var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
        if (item == null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Term `{term}` is not defined", ephemeral: true).ConfigureAwait(false);
            return;
        }
        if (renameTo is { Length: > 0 })
        {
            var check = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == renameTo).ConfigureAwait(false);
            if (check is not null)
            {
                await ctx.RespondAsync($"{Config.Reactions.Failure} Term `{renameTo}` is already defined", ephemeral: true).ConfigureAwait(false);
                return;
            }

            item.Keyword = renameTo;
        }
        if (explanation is {Length: >0 })
            item.Text = explanation;
        if (attachmentContent?.Length > 0)
        {
            item.Attachment = attachmentContent;
            item.AttachmentFilename = attachmentFilename;
        }
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync($"{Config.Reactions.Success} Term was updated", ephemeral: true).ConfigureAwait(false);
    }

    [Command("remove"), RequiresBotModRole]
    [Description("Remove a part or the whole explanation from the definition list")]
    public static async ValueTask Remove(
        SlashCommandContext ctx,
        [Description("Term to remove"),  SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string term,
        [Description("Specify what part to remove (default is remove explanation completely)")]
        RemoveExplanationOp part = RemoveExplanationOp.Complete
    )
    {
        term = term.ToLowerInvariant().StripQuotes();
        await using var db = new BotDb();
        var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
        if (item is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Term `{term}` is not defined", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var msg = $"{Config.Reactions.Success} Removed ";
        if (part is RemoveExplanationOp.AttachmentOnly)
        {
            item.Attachment = null;
            item.AttachmentFilename = null;
            msg += $"attachment from `{term}`";
        }
        /*
        else if (part is RemoveExplanationOp.TextOnly && item.Attachment is {Length: >0})
        {
            item.Text = null;
            msg += $"explanation text from `{term}`";
        }
        */
        else
        {
            db.Explanation.Remove(item);
            msg += $"`{term}`";
        }
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ctx.RespondAsync(msg, ephemeral: true).ConfigureAwait(false);
    }

    [Command("list")]
    [Description("Saves the list of all known terms as a text file attachment")]
    public static async ValueTask List(SlashCommandContext ctx)
    {
        var ephemeral = !ctx.Channel.IsSpamChannel();
        await ctx.DeferResponseAsync(ephemeral).ConfigureAwait(false);
        await using var db = new BotDb();
        var allTerms = await db.Explanation.AsNoTracking().Select(e => e.Keyword).ToListAsync();
        await using var stream = Config.MemoryStreamManager.GetStream();
        await using var writer = new StreamWriter(stream);
        foreach (var term in allTerms)
            await writer.WriteLineAsync(term);
        await writer.FlushAsync().ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);
        var response = new DiscordInteractionResponseBuilder()
            .AsEphemeral(ephemeral)
            .AddFile("explain_list.txt", stream);
        await ctx.RespondAsync(response).ConfigureAwait(false);
    }
    
    [Command("dump")]
    [Description("Save explanation content as a file attachment")]
    public static async ValueTask Dump(
        SlashCommandContext ctx,
        [Description("Term to dump"), SlashAutoCompleteProvider<ExplainAutoCompleteProvider>]
        string term
    )
    {
        await using var db = new BotDb();
        var item = await db.Explanation.FirstOrDefaultAsync(e => e.Keyword == term).ConfigureAwait(false);
        if (item is null)
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Term `{term}` is not defined",  ephemeral: true).ConfigureAwait(false);
            return;
        }

        var result = new DiscordInteractionResponseBuilder().AsEphemeral();
        await using var textStream = Config.MemoryStreamManager.GetStream(Encoding.UTF8.GetBytes(item.Text));
        if (item is { Text.Length: > 0 })
            result.AddFile($"{term}.txt", textStream);

        if (item is not { Attachment.Length: >0 })
        {
            await ctx.RespondAsync(result).ConfigureAwait(false);
            return;
        }
        
        await using var stream = Config.MemoryStreamManager.GetStream(item.Attachment);
        result.AddFile(item.AttachmentFilename!, stream);
        await ctx.RespondAsync(result).ConfigureAwait(false);
    }

    [Command("error")]
    [Description("Information about a Win32 or Linux system error")]
    public static async ValueTask Error(
        SlashCommandContext ctx,
        [Description("Error code (should start with 0x for hex code, otherwise it's interpreted as decimal)")]
        string code,
        [Description("OS type")]
        OsType osType = OsType.Windows)
    {
        if (osType is OsType.Linux && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Access to Linux error code descriptions is not available at the moment", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!(code.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
              && int.TryParse(code[2..], NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out var intCode))
            && !int.TryParse(code, out intCode)
            && !int.TryParse(code, NumberStyles.HexNumber, NumberFormatInfo.InvariantInfo, out intCode))
        {
            await ctx.RespondAsync($"{Config.Reactions.Failure} Failed to parse {code} as an error code.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var ephemeral = !ctx.Channel.IsSpamChannel();
        if (osType is OsType.Windows)
        {
            if (Win32ErrorCodes.Map.TryGetValue(intCode, out var win32Info))
                await ctx.RespondAsync($"`0x{intCode:x8}` (`{win32Info.name}`): {win32Info.description}", ephemeral: ephemeral).ConfigureAwait(false);
            else
                await ctx.RespondAsync($"Unknown Win32 error code 0x{intCode:x8}", ephemeral: ephemeral).ConfigureAwait(false);
        }
        else
        {
            try
            {
                await ctx.RespondAsync($"`{code}`: {new Win32Exception(code).Message}", ephemeral: ephemeral).ConfigureAwait(false);
            }
            catch
            {
                await ctx.RespondAsync($"Unknown error code {intCode}", ephemeral: ephemeral).ConfigureAwait(false);
            }
        }
    }

    internal enum RemoveExplanationOp
    {
        Complete,
        AttachmentOnly,
        //TextOnly,
    }
    
    internal static async ValueTask<(Explanation? explanation, string? fuzzyMatch, double score)> LookupTerm(string term)
    {
        await using var db = new BotDb();
        string? fuzzyMatch = null;
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

    internal static async ValueTask<bool> SendExplanationAsync((Explanation? explanation, string? fuzzyMatch, double score) termLookupResult, string term, DiscordMessage sourceMessage, bool useReply, bool ping = false)
    {
        try
        {
            if (termLookupResult is { explanation: not null, score: >0.5 })
            {
                var usedReply = false;
                DiscordMessageBuilder msgBuilder;
                if (!string.IsNullOrEmpty(termLookupResult.fuzzyMatch))
                {
                    var fuzzyNotice = $"Showing explanation for `{termLookupResult.fuzzyMatch}`:";
#if DEBUG
                    fuzzyNotice = $"Showing explanation for `{termLookupResult.fuzzyMatch}` ({termLookupResult.score:0.######}):";
#endif
                    msgBuilder = new DiscordMessageBuilder().WithContent(fuzzyNotice);
                    if (useReply)
                        msgBuilder.WithReply(sourceMessage.Id, ping);
                    if (ping)
                        msgBuilder.WithAllowedMention(useReply ? RepliedUserMention.All : UserMention.All);
                    await sourceMessage.Channel!.SendMessageAsync(msgBuilder).ConfigureAwait(false);
                    usedReply = true;
                }

                var explain = termLookupResult.explanation;
                StatsStorage.IncExplainStat(explain.Keyword);
                msgBuilder = new DiscordMessageBuilder().WithContent(explain.Text);
                if (!usedReply && useReply)
                    msgBuilder.WithReply(sourceMessage.Id, ping);
                if (ping)
                    msgBuilder.WithAllowedMention(useReply ? RepliedUserMention.All : UserMention.All);
                if (explain.Attachment is not { Length: > 0 })
                {
                    await sourceMessage.Channel!.SendMessageAsync(msgBuilder).ConfigureAwait(false);
                    return true;
                }
                
                await using var memStream = Config.MemoryStreamManager.GetStream(explain.Attachment);
                memStream.Seek(0, SeekOrigin.Begin);
                msgBuilder.AddFile(explain.AttachmentFilename!, memStream);
                await sourceMessage.Channel!.SendMessageAsync(msgBuilder).ConfigureAwait(false);
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
}
