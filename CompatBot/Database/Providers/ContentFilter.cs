using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.EntityFrameworkCore;
using NReco.Text;
using Microsoft.Extensions.Caching.Memory;

namespace CompatBot.Database.Providers;

internal static class ContentFilter
{
    private static Dictionary<FilterContext, AhoCorasickDoubleArrayTrie<Piracystring>?> filters = new();
    private static readonly MemoryCache ResponseAntispamCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(5)});
    private static readonly MemoryCache ReportAntispamCache = new(new MemoryCacheOptions{ ExpirationScanFrequency = TimeSpan.FromMinutes(5)});
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(15);

    static ContentFilter() => RebuildMatcher();

    public static Task<Piracystring?> FindTriggerAsync(FilterContext ctx, string str)
    {
        if (string.IsNullOrEmpty(str))
            return Task.FromResult((Piracystring?)null);

        if (!filters.TryGetValue(ctx, out var matcher))
            return Task.FromResult((Piracystring?)null);

        Piracystring? result = null;
        matcher?.ParseText(str, h =>
        {
            if (string.IsNullOrEmpty(h.Value.ValidatingRegex) || Regex.IsMatch(str, h.Value.ValidatingRegex, RegexOptions.IgnoreCase | RegexOptions.Multiline))
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

        return Task.FromResult(result);
    }

    public static void RebuildMatcher()
    {
        var newFilters = new Dictionary<FilterContext, AhoCorasickDoubleArrayTrie<Piracystring>?>();
        using var db = new BotDb();
        foreach (FilterContext ctx in Enum.GetValues(typeof(FilterContext)))
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


    public static async Task<bool> IsClean(DiscordClient client, DiscordMessage message)
    {
        if (message.Channel.IsPrivate)
            return true;

        /*
        if (message.Author.IsBotSafeCheck())
            return true;
        */

        if (message.Author.IsCurrent)
            return true;

        var suppressActions = (FilterAction)0;
#if !DEBUG
        if (message.Author.IsWhitelisted(client, message.Channel.Guild))
        {
            if (message.Content.StartsWith('>'))
                suppressActions = FilterAction.IssueWarning | FilterAction.RemoveContent | FilterAction.Kick;
            else
                return true;
        }
#endif

        var content = new StringBuilder(message.Content).AppendLine();
        if (message.Attachments is not null)
            foreach (var attachment in message.Attachments.Where(a => a is not null))
                content.AppendLine(attachment.FileName);
        if (message.Embeds is not null)
            foreach (var embed in message.Embeds.Where(e => e is not null))
            {
                content.AppendLine(embed.Title)
                    .AppendLine(embed.Description);
                if (embed.Fields is not null)
                    foreach (var field in embed.Fields.Where(f => f is not null))
                    {
                        content.AppendLine(field.Name);
                        content.AppendLine(field.Value);
                    }
            }
        var trigger = await FindTriggerAsync(FilterContext.Chat, content.ToString()).ConfigureAwait(false);
        if (trigger == null)
            return true;

        await PerformFilterActions(client, message, trigger, suppressActions).ConfigureAwait(false);
        return (trigger.Actions & ~suppressActions & (FilterAction.IssueWarning | FilterAction.RemoveContent)) == 0;
    }

    public static async Task PerformFilterActions(DiscordClient client, DiscordMessage message, Piracystring trigger, FilterAction ignoreFlags = 0, string? triggerContext = null, string? infraction = null, string? warningReason = null)
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
                var author = client.GetMember(message.Author);
                var username = author?.GetMentionWithNickname() ?? message.Author.GetUsernameWithNickname(client);
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
                await Warnings.AddAsync(client, message, message.Author.Id, message.Author.Username, client.CurrentUser, warningReason ?? "Mention of piracy", message.Content.Sanitize()).ConfigureAwait(false);
                completedActions.Add(FilterAction.IssueWarning);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Couldn't issue warning in #{message.Channel.Name}");
            }
        }

        if (trigger.Actions.HasFlag(FilterAction.ShowExplain)
            && !ignoreFlags.HasFlag(FilterAction.ShowExplain)
            && !string.IsNullOrEmpty(trigger.ExplainTerm))
        {
            var result = await Explain.LookupTerm(trigger.ExplainTerm).ConfigureAwait(false);
            await Explain.SendExplanation(result, trigger.ExplainTerm, message, true).ConfigureAwait(false);
        }

        if (trigger.Actions.HasFlag(FilterAction.Kick)
            && !ignoreFlags.HasFlag(FilterAction.Kick))
        {
            try
            {
                if (client.GetMember(message.Channel.Guild, message.Author) is DiscordMember mem
                    && !mem.Roles.Any())
                {
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
        foreach (FilterAction fa in Enum.GetValues(typeof(FilterAction)))
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
           && Regex.Match(context, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline) is { Success: true } m
           && m.Groups.Count > 0
            ? m.Groups[0].Value.Trim(256)
            : null;
}