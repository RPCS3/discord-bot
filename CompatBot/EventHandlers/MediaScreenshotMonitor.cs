using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.EventArgs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.EventHandlers
{
    internal sealed class MediaScreenshotMonitor
    {
        private readonly DiscordClient client;
        private readonly ComputerVisionClient cvClient = new(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
        private readonly SemaphoreSlim workSemaphore = new(0);
        private readonly ConcurrentQueue<(MessageCreateEventArgs evt, Guid readOperationId)> workQueue = new ConcurrentQueue<(MessageCreateEventArgs args, Guid readOperationId)>();
        public static int MaxQueueLength { get; private set; }

        internal MediaScreenshotMonitor(DiscordClient client)
        {
            this.client = client;
        }

        public async Task OnMessageCreated(DiscordClient _, MessageCreateEventArgs evt)
        {
            var message = evt.Message;
            if (message == null)
                return;

            if (!Config.Moderation.OcrChannels.Contains(evt.Channel.Id))
                return;

#if !DEBUG
            if (message.Author.IsBotSafeCheck())
                return;

            if (message.Author.IsSmartlisted(client))
                return;
#endif

            if (!message.Attachments.Any())
                return;

            var images = Vision.GetImageAttachment(message).ToList();
            var tasks = new List<Task<ReadHeaders>>(images.Count);
            foreach (var img in images)
                tasks.Add(cvClient.ReadAsync(img.Url, cancellationToken: Config.Cts.Token));
            foreach (var t in tasks)
            {
                try
                {
                    var headers = await t.ConfigureAwait(false);
                    workQueue.Enqueue((evt, new(new Uri(headers.OperationLocation).Segments.Last())));
                    workSemaphore.Release();
                }
                catch (Exception ex)
                {
                    Config.Log.Warn(ex, "Failed to create a new text recognition task");
                }
            }
        }

        public async Task ProcessWorkQueue()
        {
            if (string.IsNullOrEmpty(Config.AzureComputerVisionKey))
                return;

            Guid? reEnqueueId = null;
            do
            {
                await workSemaphore.WaitAsync(Config.Cts.Token).ConfigureAwait(false);
                if (Config.Cts.IsCancellationRequested)
                    return;

                MaxQueueLength = Math.Max(MaxQueueLength, workQueue.Count);
                if (!workQueue.TryDequeue(out var item))
                    continue;

                if (item.readOperationId == reEnqueueId)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    reEnqueueId = null;
                    if (Config.Cts.IsCancellationRequested)
                        return;
                }

                try
                {
                    var result = await cvClient.GetReadResultAsync(item.readOperationId, Config.Cts.Token).ConfigureAwait(false);
                    if (result.Status == OperationStatusCodes.Succeeded)
                    {
                        if (result.AnalyzeResult?.ReadResults?.SelectMany(r => r.Lines).Any() ?? false)
                        {
                            var cnt = true;
                            var prefix = $"[{item.evt.Message.Id % 100:00}]";
                            var ocrTextBuf = new StringBuilder($"OCR result of message <{item.evt.Message.JumpLink}>:").AppendLine();
                            Config.Log.Debug($"{prefix} OCR result of message {item.evt.Message.JumpLink}:");
                            var duplicates = new HashSet<string>();
                            foreach (var r in result.AnalyzeResult.ReadResults)
                            foreach (var l in r.Lines)
                            {
                                ocrTextBuf.AppendLine(l.Text.Sanitize());
                                Config.Log.Debug($"{prefix} {l.Text}");
                                if (cnt
                                    && await ContentFilter.FindTriggerAsync(FilterContext.Chat, l.Text).ConfigureAwait(false) is Piracystring hit
                                    && duplicates.Add(hit.String))
                                {
                                    FilterAction suppressFlags = 0;
                                    if ("media".Equals(item.evt.Channel.Name))
                                        suppressFlags = FilterAction.SendMessage | FilterAction.ShowExplain;
                                    await ContentFilter.PerformFilterActions(
                                        client,
                                        item.evt.Message,
                                        hit,
                                        suppressFlags,
                                        l.Text,
                                        "🖼 Screenshot of a pirated game",
                                        "Screenshot of a pirated game"
                                    ).ConfigureAwait(false);
                                    cnt &= !hit.Actions.HasFlag(FilterAction.RemoveContent) && !hit.Actions.HasFlag(FilterAction.IssueWarning);
                                }
                            }
                            var ocrText = ocrTextBuf.ToString();
                            var hasVkDiagInfo = ocrText.Contains("Vulkan Diagnostics Tool v")
                                                || ocrText.Contains("VkDiag Version:");
                            if (!cnt || hasVkDiagInfo)
                                try
                                {
                                    var botSpamCh = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                                    await botSpamCh.SendAutosplitMessageAsync(ocrTextBuf, blockStart: "", blockEnd: "").ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Config.Log.Warn(ex);
                                }
                        }
                    }
                    else if (result.Status == OperationStatusCodes.NotStarted || result.Status == OperationStatusCodes.Running)
                    {
                        workQueue.Enqueue(item);
                        reEnqueueId ??= item.readOperationId;
                        workSemaphore.Release();
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e);
                }
            } while (!Config.Cts.IsCancellationRequested);
        }
    }
}
