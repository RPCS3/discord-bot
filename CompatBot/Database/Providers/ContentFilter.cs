using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NReco.Text;

namespace CompatBot.Database.Providers;

internal static class ContentFilter
{
    private static Dictionary<FilterContext, AhoCorasickDoubleArrayTrie<Piracystring>?> filters = new();
    private static readonly MemoryCache ResponseAntispamCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(5)});
    private static readonly MemoryCache ReportAntispamCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(5)});
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(15);

    static ContentFilter() => RebuildMatcher();

    public static ValueTask<Piracystring?> FindTriggerAsync(FilterContext ctx, string str)
    {
        str = str.TrimEnd();
        if (str is not {Length: >0})
            return ValueTask.FromResult((Piracystring?)null);

        if (!filters.TryGetValue(ctx, out var matcher))
            return ValueTask.FromResult((Piracystring?)null);

        Piracystring? result = null;
        matcher?.ParseText(str, h =>
        {
            if (string.IsNullOrEmpty(h.Value.ValidatingRegex) || Regex.IsMatch(str, h.Value.ValidatingRegex, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.ExplicitCapture))
            {
                result = h.Value;
                Config.Log.Info($"Triggered content filter #{h.Value.Id} ({h.Value.String}; regex={h.Value.ValidatingRegex}) at idx {h.Begin} of message string '{str}'");
                return !h.Value.Actions.HasFlag(FilterAction.IssueWarning);
            }
            return true;
        });

        if (result is null && ctx == FilterContext.Chat)
        {
            str = str.StripInvisibleAndDiacritics();
            matcher?.ParseText(str, h =>
            {
                if (string.IsNullOrEmpty(h.Value.ValidatingRegex) || Regex.IsMatch(str, h.Value.ValidatingRegex, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                {
                    result = h.Value;
                    Config.Log.Info($"Triggered content filter #{h.Value.Id} ({h.Value.String}; regex={h.Value.ValidatingRegex}) at idx {h.Begin} of string '{str}'");
                    return !h.Value.Actions.HasFlag(FilterAction.IssueWarning);
                }
                return true;
            });
        }

        return ValueTask.FromResult(result);
    }

    public static void RebuildMatcher()
    {
        var newFilters = new Dictionary<FilterContext, AhoCorasickDoubleArrayTrie<Piracystring>?>();
        using var db = BotDb.OpenRead();
        foreach (FilterContext ctx in Enum.GetValues<FilterContext>())
        {
            var triggerList = db.Piracystring.Where(ps => ps.Disabled == false && ps.Context.HasFlag(ctx)).AsNoTracking()
                .AsEnumerable()
                .Concat(db.SuspiciousString.AsNoTracking().AsEnumerable().Select(ss => new Piracystring
                    {
                        String = ss.String,
                        Actions = FilterAction.RemoveContent, // | FilterAction.IssueWarning | FilterAction.SendMessage,
                        Context = FilterContext.Log | FilterContext.Chat,
                        CustomMessage = "Please follow the rules and dump your own copy of game yourself. You **can not download** game files from the internet. Repeated offence may result in a ban.",
                    })
                ).ToList();

            if (triggerList.Count == 0)
                newFilters[ctx] = null;
            else
            {
                try
                {
                    newFilters[ctx] = new(triggerList.ToDictionary(s => s.String, s => s), true);
                }
                catch (ArgumentException)
                {
                    var duplicate = (
                        from ps in triggerList
                        group ps by ps.String into g
                        where g.Count() > 1
                        select g.Key
                    ).ToList();
                    Config.Log.Error($"Duplicate triggers defined for Context {ctx}: {string.Join(", ", duplicate)}");
                    var triggerDictionary = new Dictionary<string, Piracystring>();
                    foreach (var ps in triggerList)
                        triggerDictionary[ps.String] = ps;
                    newFilters[ctx] = new(triggerDictionary, true);
                }
            }
        }
        filters = newFilters;
    }

    public static async ValueTask<bool> IsClean(DiscordClient client, DiscordMessage message)
    {
        if (message.Channel?.IsPrivate is true)
            return true;

        if (message.Channel?.Id == Config.BotLogId)
            return true;

        /*
        if (message.Author.IsBotSafeCheck())
            return true;
        */

        if (message.Author == client.CurrentUser)
            return true;

        var suppressActions = (FilterAction)0;
        if (message.Timestamp.UtcDateTime.AddDays(1) < DateTime.UtcNow)
            suppressActions = FilterAction.SendMessage | FilterAction.ShowExplain;

#if !DEBUG
        if (await message.Author.IsWhitelistedAsync(client, message.Channel.Guild).ConfigureAwait(false))
        {
            if (message.Content.StartsWith('>'))
                suppressActions = FilterAction.IssueWarning | FilterAction.RemoveContent | FilterAction.Kick;
            else
                return true;
        }
#endif

        var content = new StringBuilder();
        DumpMessageContent(message, content);
        if (message.Reference is {Type: DiscordMessageReferenceType.Forward} refMsg)
        {
            try
            {
                var msg = await client.GetMessageAsync(refMsg.Channel, refMsg.Message.Id).ConfigureAwait(false);
                if (msg is not null)
                {
                    content.AppendLine();
                    DumpMessageContent(msg, content);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get forwarded message");
            }
        }
        
        var trigger = await FindTriggerAsync(FilterContext.Chat, content.ToString()).ConfigureAwait(false);
        if (trigger is null)
            return true;

        await PerformFilterActions(client, message, trigger, suppressActions).ConfigureAwait(false);
        return (trigger.Actions & ~suppressActions & (FilterAction.IssueWarning | FilterAction.RemoveContent)) == 0;
    }

    public static async ValueTask PerformFilterActions(
        DiscordClient client,
        DiscordMessage message,
        Piracystring trigger,
        FilterAction ignoreFlags = 0,
        string? triggerContext = null,
        string? infraction = null,
        string? warningReason = null
    )
    {
        var severity = ReportSeverity.Low;
        var completedActions = new List<FilterAction>();
        if (trigger.Actions.HasFlag(FilterAction.RemoveContent) && !ignoreFlags.HasFlag(FilterAction.RemoveContent))
        {
            try
            {
                DeletedMessagesMonitor.RemovedByBotCache.Set(message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                await message.Channel.DeleteMessageAsync(message, $"Removed according to filter '{trigger}'").ConfigureAwait(false);
                completedActions.Add(FilterAction.RemoveContent);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
                severity = ReportSeverity.High;
            }
            try
            {
                var author = await client.GetMemberAsync(message.Author).ConfigureAwait(false);
                var username = author?.GetMentionWithNickname() ?? await message.Author.GetUsernameWithNicknameAsync(client).ConfigureAwait(false);
                Config.Log.Debug($"Removed message from {username} in #{message.Channel.Name}: {message.Content}");
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        }

        if (trigger.Actions.HasFlag(FilterAction.SendMessage) && !ignoreFlags.HasFlag(FilterAction.SendMessage))
        {
            try
            {
                ResponseAntispamCache.TryGetValue(message.Author.Id, out int counter);
                if (counter < 3)
                {

                    var msgContent = trigger.CustomMessage;
                    if (string.IsNullOrEmpty(msgContent))
                    {
                        var rules = await client.GetChannelAsync(Config.BotRulesChannelId).ConfigureAwait(false);
                        msgContent = $"Please follow the {rules.Mention} and do not post/discuss anything piracy-related on this server.\nYou always **must** dump your own copy of the game yourself. You **can not** download game files from the internet.\nRepeated offence may result in a ban.";
                    }
                    await message.Channel.SendMessageAsync($"{message.Author.Mention} {msgContent}").ConfigureAwait(false);
                }
                ResponseAntispamCache.Set(message.Author.Id, counter + 1, CacheTime);
                completedActions.Add(FilterAction.SendMessage);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to send message in #{message.Channel.Name}");
            }
        }

        if (trigger.Actions.HasFlag(FilterAction.IssueWarning) && !ignoreFlags.HasFlag(FilterAction.IssueWarning))
        {
            try
            {
                warningReason ??= "Mention of piracy";
                var (saved, suppress, recent, total) = await Warnings.AddAsync(
                    message.Author!.Id,
                    client.CurrentUser,
                    warningReason,
                    message.Content.Sanitize()
                ).ConfigureAwait(false);
                if (saved && !suppress && message.Channel is not null)
                {
                    var msgContent = await Warnings.GetDefaultWarningMessageAsync(client, message.Author, warningReason, recent, total, client.CurrentUser).ConfigureAwait(false);
                    var msg = new DiscordMessageBuilder()
                        .WithContent(msgContent)
                        .AddMention(new UserMention(message.Author));
                    await message.Channel.SendMessageAsync(msg).ConfigureAwait(false);
                }
                completedActions.Add(FilterAction.IssueWarning);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't issue warning in #{message.Channel?.Name}");
            }
        }

        if (trigger.Actions.HasFlag(FilterAction.ShowExplain)
            && !ignoreFlags.HasFlag(FilterAction.ShowExplain)
            && trigger.ExplainTerm is { Length: >0 } term)
        {
            var result = await Explain.LookupTerm(term).ConfigureAwait(false);
            await Explain.SendExplanationAsync(result, term, message, true).ConfigureAwait(false);
        }

        if (trigger.Actions.HasFlag(FilterAction.Kick)
            && !ignoreFlags.HasFlag(FilterAction.Kick))
        {
            try
            {
                if (await client.GetMemberAsync(message.Channel?.Guild, message.Author).ConfigureAwait(false) is DiscordMember mem
                    && !mem.Roles.Any())
                {
                    try
                    {
                        await mem.SendMessageAsync("You have been kicked from the server for posting undesirable content. Please do not post it again.").ConfigureAwait(false);
                    }
                    catch {}
                    await mem.RemoveAsync("Filter action for trigger " + trigger.String).ConfigureAwait(false);
                    completedActions.Add(FilterAction.Kick);
                }
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't kick user from server");
            }
        }

        var actionList = "";
        foreach (FilterAction fa in FilterActionExtensions.ActionFlagValues)
        {
            if (trigger.Actions.HasFlag(fa) && !ignoreFlags.HasFlag(fa))
                actionList += (completedActions.Contains(fa) ? "✅" : "❌") + " " + fa + ' ';
        }

        try
        {
            ReportAntispamCache.TryGetValue(message.Author.Id, out int counter);
            if (!trigger.Actions.HasFlag(FilterAction.MuteModQueue) && !ignoreFlags.HasFlag(FilterAction.MuteModQueue) && counter < 3)
            {
                var context = triggerContext ?? message.Content;
                var matchedOn = GetMatchedScope(trigger, context);
                await client.ReportAsync(infraction ?? "🤬 Content filter hit", message, trigger.String, matchedOn, trigger.Id, context, severity, actionList).ConfigureAwait(false);
                ReportAntispamCache.Set(message.Author.Id, counter + 1, CacheTime);
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Failed to report content filter hit");
        }
    }

    public static string? GetMatchedScope(Piracystring trigger, string? context)
        => context is { Length: >0 }
           && trigger.ValidatingRegex is { Length: >0 } pattern
           && Regex.Match(context, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline) is { Success: true, Groups.Count: > 0 } m
            ? m.Groups[0].Value.Trim(256)
            : null;

    private static void DumpMessageContent(DiscordMessage message, StringBuilder content)
    {
        if (message.Content is {Length: >0})
            content.AppendLine(message.Content);
        foreach (var attachment in message.Attachments.Where(a => a.FileName is {Length: >0}))
            content.AppendLine(attachment.FileName);
        foreach (var embed in message.Embeds)
        {
            content.AppendLine(embed.Title).AppendLine(embed.Description);
            if (embed.Fields is not { Count: > 0 })
                continue;
            
            foreach (var field in embed.Fields)
            {
                content.AppendLine(field.Name);
                content.AppendLine(field.Value);
            }
        }
    }
}