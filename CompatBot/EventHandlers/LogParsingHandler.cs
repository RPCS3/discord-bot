using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Commands.Checks;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers.LogParsing;
using CompatBot.EventHandlers.LogParsing.ArchiveHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using CompatBot.Utils.Extensions;
using CompatBot.Utils.ResultFormatters;
using Microsoft.Extensions.Caching.Memory;
using ResultNet;

namespace CompatBot.EventHandlers;

public static class LogParsingHandler
{
    private static readonly char[] LinkSeparator = [' ', '>', '\r', '\n'];
    private static readonly ISourceHandler[] SourceHandlers =
    [
        new DiscordAttachmentHandler(),
        new GoogleDriveHandler(),
        new DropboxHandler(),
        new MegaHandler(),
        new OneDriveSourceHandler(),
        new YandexDiskHandler(),
        new MediafireHandler(),
        new GenericLinkHandler(),
        new PastebinHandler(),
    ];
    private static readonly IArchiveHandler[] ArchiveHandlers =
    [
        new GzipHandler(),
        new ZipHandler(),
        new RarHandler(),
        new SevenZipHandler(),
        new PlainTextHandler(),
    ];

    private static readonly SemaphoreSlim QueueLimiter = new(
        Math.Max(Config.MinLogThreads, Environment.ProcessorCount - 1),
        Math.Max(Config.MinLogThreads, Environment.ProcessorCount - 1)
    );
    private delegate void OnLog(DiscordClient client, DiscordChannel channel, DiscordMessage message, DiscordMember? requester = null, bool checkExternalLinks = false, bool force = false);
    private static event OnLog OnNewLog = EnqueueLogProcessing;

    public static Task OnMessageCreated(DiscordClient c, MessageCreatedEventArgs args)
    {
        var message = args.Message;
        if (message.Author.IsBotSafeCheck())
            return Task.CompletedTask;
        
        if (!args.Channel.IsPrivate
            && !Config.Moderation.LogParsingChannels.Contains(args.Channel.Id))
            return Task.CompletedTask;

        if (!string.IsNullOrEmpty(message.Content)
            && (message.Content.StartsWith(Config.CommandPrefix)
                || message.Content.StartsWith(Config.AutoRemoveCommandPrefix)))
            return Task.CompletedTask;

        var isSpamChannel = args.Channel.IsSpamChannel();
        var isHelpChannel = args.Channel.IsHelpChannel();
        var checkExternalLinks = isHelpChannel || isSpamChannel;
        if (!checkExternalLinks && message.Attachments is not {Count: >0})
            return Task.CompletedTask;
        
        OnNewLog(c, args.Channel, args.Message, checkExternalLinks: checkExternalLinks);
        return Task.CompletedTask;
    }

    public static async void EnqueueLogProcessing(DiscordClient client, DiscordChannel channel, DiscordMessage message, DiscordMember? requester = null, bool checkExternalLinks = false, bool force = false)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var parsedLog = false;
            var startTime = Stopwatch.StartNew();
            DiscordMessage? botMsg = null;
            var possibleHandlers = SourceHandlers
                .ToAsyncEnumerable()
                .SelectAwait(async h => await h.FindHandlerAsync(message, ArchiveHandlers).ConfigureAwait(false))
                .ToList();
            using var source = possibleHandlers.FirstOrDefault(h => h.IsSuccess())?.Data;
            var fail = possibleHandlers.FirstOrDefault(h => h is {Message.Length: >0})?.Message;
            foreach (var h in possibleHandlers)
            {
                if (ReferenceEquals(h.Data, source))
                    continue;
                
                h.Data?.Dispose();
            }
                
            var isSpamChannel = channel.IsSpamChannel();
            var isHelpChannel = channel.IsHelpChannel();
            if (source is not null)
            {
                if (!QueueLimiter.Wait(0))
                {
                    Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, TimeSpan.Zero, HttpStatusCode.TooManyRequests.ToString(), false);
                    await channel.SendMessageAsync("Log processing is rate limited, try again a bit later").ConfigureAwait(false);
                    return;
                }

                try
                {
                    Config.Log.Debug($">>>>>>> {message.Id % 100} Parsing log '{source.FileName}' from {message.Author.Username}#{message.Author.Discriminator} ({message.Author.Id}) using {source.GetType().Name} ({source.SourceFileSize} bytes)…");
                    var analyzingProgressEmbed = GetAnalyzingMsgEmbed(client);
                    var msgBuilder = new DiscordMessageBuilder()
                        .AddEmbed(await analyzingProgressEmbed.AddAuthorAsync(client, message, source).ConfigureAwait(false))
                        .WithReply(message.Id);
                    botMsg = await channel.SendMessageAsync(msgBuilder).ConfigureAwait(false);
                    parsedLog = true;

                    LogParseState? result = null, tmpResult;
                    using (var timeout = new CancellationTokenSource(Config.LogParsingTimeoutInSec))
                    {
                        using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, Config.Cts.Token);
                        var tries = 0;
                        do
                        {
                            tmpResult = await ParseLogAsync(
                                source,
                                async () => botMsg = await botMsg.UpdateOrCreateMessageAsync(
                                    channel,
                                    embed: await analyzingProgressEmbed.AddAuthorAsync(client, message, source).ConfigureAwait(false)
                                ).ConfigureAwait(false),
                                combinedTokenSource.Token
                            ).ConfigureAwait(false);
                            result ??= tmpResult;
                            tries++;
                        } while ((tmpResult is null || tmpResult.Error == LogParseState.ErrorCode.UnknownError) &&
                                 !combinedTokenSource.IsCancellationRequested && tries < 3);
                    }
                    if (result is null)
                    {
                        botMsg = await botMsg.UpdateOrCreateMessageAsync(channel, embed: (await new DiscordEmbedBuilder
                                {
                                    Description = """
                                                  Log analysis failed, most likely cause is a truncated/invalid log.
                                                  Please run the game again and re-upload a new copy.
                                                  """,
                                    Color = Config.Colors.LogResultFailed,
                                }.AddAuthorAsync(client, message, source).ConfigureAwait(false))
                                .Build()
                        ).ConfigureAwait(false);
                        Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start,
                            DateTimeOffset.UtcNow - start, HttpStatusCode.InternalServerError.ToString(), false);
                    }
                    else
                    {
                        result.ParsingTime = startTime.Elapsed;
                        try
                        {
                            if (result.Error == LogParseState.ErrorCode.PiracyDetected)
                            {
                                if (result.SelectedFilter is null)
                                {
                                    Config.Log.Error("Piracy was detected in log, but no trigger provided");
                                    result.SelectedFilter = new()
                                    {
                                        String = "Unknown trigger, plz kick 13xforever",
                                        Actions = FilterAction.IssueWarning | FilterAction.RemoveContent,
                                        Context = FilterContext.Log,
                                    };
                                }
                                var yarr = client.GetEmoji(":piratethink:", "☠");
                                result.ReadBytes = 0;
                                if (await message.Author.IsWhitelistedAsync(client, channel.Guild).ConfigureAwait(false))
                                {
                                    var piracyWarning = await result.AsEmbedAsync(client, message, source).ConfigureAwait(false);
                                    piracyWarning = piracyWarning.WithDescription("Please remove the log and issue warning to the original author of the log");
                                    botMsg = await botMsg.UpdateOrCreateMessageAsync(channel, embed: piracyWarning).ConfigureAwait(false);
                                    var matchedOn = ContentFilter.GetMatchedScope(result.SelectedFilter, result.SelectedFilterContext);
                                    await client.ReportAsync(yarr + " Pirated Release (whitelisted by role)", message,
                                        result.SelectedFilter.String, matchedOn, result.SelectedFilter.Id,
                                        result.SelectedFilterContext, ReportSeverity.Low).ConfigureAwait(false);
                                }
                                else
                                {
                                    var severity = ReportSeverity.Low;
                                    try
                                    {
                                        DeletedMessagesMonitor.RemovedByBotCache.Set(message.Id, true, DeletedMessagesMonitor.CacheRetainTime);
                                        await message.DeleteAsync("Piracy detected in log").ConfigureAwait(false);
                                    }
                                    catch (Exception e)
                                    {
                                        severity = ReportSeverity.High;
                                        Config.Log.Warn(e, $"Unable to delete message in {channel.Name}");
                                    }
                                    try
                                    {
                                        botMsg = await botMsg.UpdateOrCreateMessageAsync(channel,
                                            $"""
                                             # Pirated content detected 🏴‍☠️
                                             {message.Author.Mention}, please read carefully
                                             ### You are being denied further support until you legally dump the game
                                             Please note that the RPCS3 community and its developers do not support piracy.
                                             Most of the issues with pirated dumps occur due to them being modified in some way that prevent them from working on RPCS3.
                                             If you need help obtaining valid working dump of the game you own, please read [the quickstart guide](<https://rpcs3.net/quickstart>).
                                             """
                                        ).ConfigureAwait(false);
                                    }
                                    catch (Exception e)
                                    {
                                        Config.Log.Error(e, "Failed to send piracy warning");
                                    }
                                    try
                                    {
                                        var matchedOn = ContentFilter.GetMatchedScope(result.SelectedFilter, result.SelectedFilterContext);
                                        await client.ReportAsync(yarr + " Pirated Release", message,
                                            result.SelectedFilter.String, matchedOn, result.SelectedFilter.Id,
                                            result.SelectedFilterContext, severity).ConfigureAwait(false);
                                    }
                                    catch (Exception e)
                                    {
                                        Config.Log.Error(e, "Failed to send piracy report");
                                    }
                                    //if (!(message.Channel!.IsPrivate || message.Channel.Name.Contains("spam")))
                                    {
                                        var reason = "Logs showing use of pirated content";
                                        var (saved, suppress, recent, total) = await Warnings.AddAsync(
                                            message.Author.Id,
                                            client.CurrentUser,
                                            reason,
                                            $"{result.SelectedFilter.String} - {result.SelectedFilterContext?.Sanitize()}"
                                        );
                                        if (saved && !suppress)
                                        {
                                            var content = await Warnings.GetDefaultWarningMessageAsync(client, message.Author, reason, recent, total, client.CurrentUser).ConfigureAwait(false);
                                            var msg = new DiscordMessageBuilder()
                                                .WithContent(content)
                                                .AddMention(new UserMention(message.Author));
                                            await message.Channel!.SendMessageAsync(msg).ConfigureAwait(false);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                if (result.SelectedFilter is not null)
                                {
                                    var ignoreFlags = FilterAction.IssueWarning | FilterAction.SendMessage | FilterAction.ShowExplain;
                                    await ContentFilter.PerformFilterActions(client, message, result.SelectedFilter,
                                        ignoreFlags, result.SelectedFilterContext!).ConfigureAwait(false);
                                }

                                if (!force
                                    && string.IsNullOrEmpty(message.Content)
                                    && !isSpamChannel
                                    && !await message.Author.IsSmartlistedAsync(client, message.Channel.Guild).ConfigureAwait(false))
                                {
                                    var threshold = DateTime.UtcNow.AddMinutes(-15);
                                    var previousMessages = await channel.GetMessagesBeforeCachedAsync(message.Id).ConfigureAwait(false);
                                    previousMessages = previousMessages.TakeWhile((msg, num) =>
                                        num < 15 || msg.Timestamp.UtcDateTime > threshold).ToList();
                                    if (!previousMessages.Any(m =>
                                            m.Author == message.Author && !string.IsNullOrEmpty(m.Content)))
                                    {
                                        var botSpamChannel = await client.GetChannelAsync(Config.BotSpamId).ConfigureAwait(false);
                                        if (isHelpChannel)
                                            await botMsg.UpdateOrCreateMessageAsync(
                                                channel,
                                                $"{message.Author.Mention} please describe the issue if you require help, or upload log in {botSpamChannel.Mention} if you only need to check your logs automatically"
                                            ).ConfigureAwait(false);
                                        else
                                        {
                                            Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start,
                                                DateTimeOffset.UtcNow - start, HttpStatusCode.NoContent.ToString(),
                                                true);
                                            var helpChannel = await LimitedToSpecificChannelsCheck
                                                .GetHelpChannelAsync(client, channel, message.Author)
                                                .ConfigureAwait(false);
                                            if (helpChannel is not null)
                                                await botMsg.UpdateOrCreateMessageAsync(
                                                    channel,
                                                    $"{message.Author.Mention} if you require help, please ask in {helpChannel.Mention}, and describe your issue first, " +
                                                    $"or upload log in {botSpamChannel.Mention} if you only need to check your logs automatically"
                                                ).ConfigureAwait(false);
                                        }
                                        return;
                                    }
                                }

                                botMsg = await botMsg.UpdateOrCreateMessageAsync(channel,
                                    //requester is null ? null : $"Analyzed log from {client.GetMember(channel.Guild, message.Author)?.GetUsernameWithNickname()} by request from {requester.Mention}:",
                                    embed: await result.AsEmbedAsync(client, message, source).ConfigureAwait(false)
                                ).ConfigureAwait(false);
                            }
                            Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start,
                                DateTimeOffset.UtcNow - start, HttpStatusCode.OK.ToString(), true);
                        }
                        catch (Exception e)
                        {
                            Config.Log.Error(e, "Sending log results failed");
                        }
                    }
                    return;
                }
                finally
                {
                    QueueLimiter.Release();
                    if (parsedLog)
                        Config.Log.Debug($"<<<<<<< {message.Id % 100} Finished parsing in {startTime.Elapsed}");
                }
            }
            if (!string.IsNullOrEmpty(fail)
                && (isHelpChannel || isSpamChannel))
            {
                Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, DateTimeOffset.UtcNow - start, HttpStatusCode.InternalServerError.ToString(), false);
                await channel.SendMessageAsync($"{message.Author.Mention} {fail}").ConfigureAwait(false);
                return;
            }

            var potentialLogExtension = message.Attachments.Select(a => Path.GetExtension(a.FileName)?.ToUpperInvariant().TrimStart('.')).FirstOrDefault();
            switch (potentialLogExtension)
            {
                case "TXT":
                {
                    await channel.SendMessageAsync(
                        $"{message.Author.Mention}, please upload the full RPCS3.log.gz (or RPCS3.log with a zip/rar icon) file " +
                        "after closing the emulator instead of copying the logs from RPCS3's interface, " +
                        "as it doesn't contain all the required information."
                    ).ConfigureAwait(false);
                    Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, DateTimeOffset.UtcNow - start, HttpStatusCode.BadRequest.ToString(), true);
                    return;
                }
            }

            if (string.IsNullOrEmpty(message.Content))
            {
                Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, DateTimeOffset.UtcNow - start, HttpStatusCode.NoContent.ToString(), true);
                return;
            }

            var linkStart = message.Content.IndexOf("http", StringComparison.Ordinal);
            if (linkStart > -1)
            {
                var link = message.Content[linkStart..].Split(LinkSeparator, 2)[0];
                if (link.Contains(".log", StringComparison.InvariantCultureIgnoreCase) || link.Contains("rpcs3.zip", StringComparison.CurrentCultureIgnoreCase))
                {
                    await channel.SendMessageAsync("If you intended to upload a log file please re-upload it directly to discord").ConfigureAwait(false);
                    Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, DateTimeOffset.UtcNow - start, HttpStatusCode.BadRequest.ToString(), true);
                }
            }
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Error parsing log");
            Config.TelemetryClient?.TrackRequest(nameof(LogParsingHandler), start, DateTimeOffset.UtcNow - start, HttpStatusCode.InternalServerError.ToString(), false);
            Config.TelemetryClient?.TrackException(e);
        }
    }

    public static async ValueTask<LogParseState?> ParseLogAsync(ISource source, Func<Task> onProgressAsync, CancellationToken cancellationToken)
    {
        LogParseState? result = null;
        try
        {
            try
            {
                var pipe = new Pipe();
                var fillPipeTask = source.FillPipeAsync(pipe.Writer, cancellationToken);
                var readPipeTask = LogParser.ReadPipeAsync(pipe.Reader, cancellationToken);
                do
                {
                    await Task.WhenAny(readPipeTask, Task.Delay(5000, cancellationToken)).ConfigureAwait(false);
                    if (!readPipeTask.IsCompleted)
                        await onProgressAsync().ConfigureAwait(false);
                } while (!readPipeTask.IsCompleted && !cancellationToken.IsCancellationRequested);
                result = await readPipeTask.ConfigureAwait(false);
                await fillPipeTask.ConfigureAwait(false);
            }
            catch (Exception pre)
            {
                if (pre is not OperationCanceledException)
                    Config.Log.Error(pre);
                if (result is null)
                    throw;
            }

            result.TotalBytes = source.LogFileSize;
            if (result.FilterTriggers.Any())
            {
                var (f, c) = result.FilterTriggers.Values.FirstOrDefault(ft => ft.filter.Actions.HasFlag(FilterAction.IssueWarning));
                if (f is null)
                    (f, c) = result.FilterTriggers.Values.FirstOrDefault(ft => ft.filter.Actions.HasFlag(FilterAction.RemoveContent));
                if (f is null)
                    (f, c) = result.FilterTriggers.Values.FirstOrDefault();
                result.SelectedFilter = f;
                result.SelectedFilterContext = c;
            }
#if DEBUG
            Config.Log.Debug("~~~~~~~~~~~~~~~~~~~~");
            Config.Log.Debug("Extractor hit stats (CPU time, s / total hits):");
            foreach (var (key, (count, time)) in result.ExtractorHitStats.OrderByDescending(kvp => kvp.Value.regexTime))
            {
                var ttime = TimeSpan.FromTicks(time).TotalSeconds;
                var msg = $"{ttime:0.000}/{count} ({ttime/count:0.000000}): {key}";
                if (count > 100000 || ttime > 20)
                    Config.Log.Fatal(msg);
                else if (count > 10000 || ttime > 10)
                    Config.Log.Error(msg);
                else if (count > 1000 || ttime > 5)
                    Config.Log.Warn(msg);
                else if (count > 100 || ttime > 1)
                    Config.Log.Info(msg);
                else
                    Config.Log.Debug(msg);
            }

            Config.Log.Debug("~~~~~~~~~~~~~~~~~~~~");
            Config.Log.Debug("Syscall stats:");
            int serialCount = result.Syscalls.Count, functionCount = 0;
            foreach (var funcStats in result.Syscalls.Values)
                functionCount += funcStats.Count;
            Config.Log.Debug("Product keys: " + serialCount);
            Config.Log.Debug("Functions: " + functionCount);
            Config.Log.Debug("Saving syscall information…");
            var sw = Stopwatch.StartNew();
#endif
            await SyscallInfoProvider.SaveAsync(result.Syscalls).ConfigureAwait(false);
#if DEBUG
            Config.Log.Debug("Saving syscall information took " + sw.Elapsed);
#endif
        }
        catch (Exception e)
        {
            Config.Log.Error(e, "Log parsing failed");
        }
        return result;
    }

    private static DiscordEmbedBuilder GetAnalyzingMsgEmbed(DiscordClient client)
    {
        var indicator = client.GetEmoji(":kannamag:", Config.Reactions.PleaseWait);
        return new()
        {
            Description = $"{indicator} Looking at the log, please wait… {indicator}",
            Color = Config.Colors.LogUnknown,
        };
    }
}