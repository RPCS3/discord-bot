﻿using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.EventHandlers.LogParsing;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class LogInfoHandler
    {
        private static readonly char[] linkSeparator = { ' ', '>', '\r', '\n' };
        private static readonly ISourceHandler[] handlers =
        {
            new GzipHandler(),
            new PlainTextHandler(),
            new ZipHandler(),
        };

        private static readonly SemaphoreSlim QueueLimiter = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount / 2), Math.Max(1, Environment.ProcessorCount / 2));
        private delegate void OnLog(MessageCreateEventArgs args);
        private static event OnLog OnNewLog;

        static LogInfoHandler()
        {
            OnNewLog += BackgroundProcessor;
        }

        public static Task OnMessageCreated(MessageCreateEventArgs args)
        {
            var message = args.Message;
            if (message.Author.IsBot)
                return Task.CompletedTask;

            if (!string.IsNullOrEmpty(message.Content) && message.Content.StartsWith(Config.CommandPrefix))
                return Task.CompletedTask;

            OnNewLog(args);
            return Task.CompletedTask;
        }

        public static async void BackgroundProcessor(MessageCreateEventArgs args)
        {
            var message = args.Message;
            if (!QueueLimiter.Wait(0))
            {
                await args.Channel.SendMessageAsync("Log processing is rate limited, try again a bit later").ConfigureAwait(false);
                return;
            }

            bool parsedLog = false;
            var startTime = Stopwatch.StartNew();
            try
            {
                foreach (var attachment in message.Attachments.Where(a => a.FileSize < Config.AttachmentSizeLimit))
                foreach (var handler in handlers)
                    if (await handler.CanHandleAsync(attachment).ConfigureAwait(false))
                    {
                        await args.Channel.TriggerTypingAsync().ConfigureAwait(false);
                        Console.WriteLine($">>>>>>> {message.Id%100} Parsing log from attachment {attachment.FileName} ({attachment.FileSize})...");
                        parsedLog = true;
                        LogParseState result = null;
                        try
                        {
                            var pipe = new Pipe();
                            var fillPipeTask = handler.FillPipeAsync(attachment, pipe.Writer);
                            result = await LogParser.ReadPipeAsync(pipe.Reader).ConfigureAwait(false);
                            await fillPipeTask.ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            args.Client.DebugLogger.LogMessage(LogLevel.Error, "", "Log parsing failed: " + e, DateTime.Now);
                        }
                        if (result == null)
                            await args.Channel.SendMessageAsync("Log analysis failed, most likely cause is a truncated/invalid log. Please run the game again and reupload the new copy.").ConfigureAwait(false);
                        else
                        {
                            try
                            {
                                if (result.Error == LogParseState.ErrorCode.PiracyDetected)
                                {
                                    if (args.Author.IsWhitelisted(args.Client, args.Guild))
                                    {
                                        await Task.WhenAll(
                                            args.Channel.SendMessageAsync("I see wha' ye did thar ☠"),
                                            args.Client.ReportAsync("Pirated Release (whitelisted by role)", args.Message, result.PiracyTrigger, result.PiracyContext)
                                        ).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        bool needsAttention = false;
                                        try
                                        {
                                            await message.DeleteAsync("Piracy detected in log").ConfigureAwait(false);
                                        }
                                        catch (Exception e)
                                        {
                                            needsAttention = true;
                                            args.Client.DebugLogger.LogMessage(LogLevel.Warning, "", $"Unable to delete message in {args.Channel.Name}: {e.Message}", DateTime.Now);
                                        }
                                        await args.Channel.SendMessageAsync(embed: await result.AsEmbedAsync(args.Client, args.Message).ConfigureAwait(false)).ConfigureAwait(false);
                                        await Task.WhenAll(
                                            args.Client.ReportAsync("Pirated Release", args.Message, result.PiracyTrigger, result.PiracyContext, needsAttention),
                                            Warnings.AddAsync(args.Client, args.Message, args.Message.Author.Id, args.Message.Author.Username, args.Client.CurrentUser,
                                                "Pirated Release", $"{message.Content.Sanitize()} - {result.PiracyTrigger}")
                                        );
                                    }
                                }
                                else
                                    await args.Channel.SendMessageAsync(embed: await result.AsEmbedAsync(args.Client, args.Message).ConfigureAwait(false)).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                args.Client.DebugLogger.LogMessage(LogLevel.Error, "", "Sending log results failed: " + e, DateTime.Now);
                            }
                        }
                        return;
                    }

                if (string.IsNullOrEmpty(message.Content) || !"help".Equals(args.Channel.Name, StringComparison.InvariantCultureIgnoreCase))
                    return;

                var linkStart = message.Content.IndexOf("http");
                if (linkStart > -1)
                {
                    var link = message.Content.Substring(linkStart).Split(linkSeparator, 2)[0];
                    if (link.Contains(".log", StringComparison.InvariantCultureIgnoreCase) || link.Contains("rpcs3.zip", StringComparison.CurrentCultureIgnoreCase))
                        await args.Channel.SendMessageAsync("If you intended to upload a log file please reupload it directly to discord").ConfigureAwait(false);
                }
            }
            finally
            {
                QueueLimiter.Release();
                if (parsedLog)
                    Console.WriteLine($"<<<<<<< {message.Id % 100} Finished parsing in {startTime.Elapsed}");
            }
        }
    }
}
