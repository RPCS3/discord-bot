using System;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using CompatApiClient;
using CompatBot.Commands;
using CompatBot.LogParsing;
using CompatBot.LogParsing.SourceHandlers;
using CompatBot.ResultFormatters;
using CompatBot.Utils;
using DSharpPlus;
using DSharpPlus.EventArgs;

namespace CompatBot.EventHandlers
{
    internal static class LogInfoHandler
    {
        private static readonly ISourceHandler[] handlers =
        {
            new GzipHandler(),
            new PlainTextHandler(),
            new ZipHandler(),
        };

        private static readonly char[] linkSeparator = {' ', '>', '\r', '\n'};

        public static async Task OnMessageCreated(MessageCreateEventArgs args)
        {
            var message = args.Message;
            if (message.Author.IsBot)
                return;

            if (!string.IsNullOrEmpty(message.Content) && message.Content.StartsWith(Config.CommandPrefix))
                return;

            foreach (var attachment in message.Attachments.Where(a => a.FileSize < Config.AttachmentSizeLimit))
            foreach (var handler in handlers)
                if (await handler.CanHandleAsync(attachment).ConfigureAwait(false))
                {
                    await args.Channel.TriggerTypingAsync().ConfigureAwait(false);
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
                        await args.Channel.SendMessageAsync(embed: await result.AsEmbedAsync(args.Client, args.Message).ConfigureAwait(false)).ConfigureAwait(false);
                        if (result.Error == LogParseState.ErrorCode.PiracyDetected)
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
                            await Task.WhenAll(
                                args.Client.ReportAsync("Pirated Release", args.Message, result.PiracyTrigger, result.PiracyContext, needsAttention),
                                Warnings.AddAsync(args.Client, args.Message, args.Message.Author.Id, args.Message.Author.Username, args.Client.CurrentUser,
                                                  "Pirated Release", $"{message.Content.Sanitize()} - {result.PiracyTrigger}")
                            );
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
    }
}
