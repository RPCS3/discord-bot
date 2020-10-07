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
using DSharpPlus.Interactivity;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.EventHandlers
{
    internal sealed class MediaScreenshotMonitor
    {
        private readonly DiscordClient client;
        private readonly ComputerVisionClient cvClient = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
        private readonly SemaphoreSlim workSemaphore = new SemaphoreSlim(0);
        private readonly ConcurrentQueue<(MessageCreateEventArgs evt, string readOperationId)> workQueue = new ConcurrentQueue<(MessageCreateEventArgs args, string readOperationId)>();
        public static int MaxQueueLength { get; private set; } = 0;

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
            var tasks = new List<Task<BatchReadFileHeaders>>(images.Count);
            foreach (var img in images)
                tasks.Add(cvClient.BatchReadFileAsync(img.Url, Config.Cts.Token));
            foreach (var t in tasks)
            {
                try
                {
                    var headers = await t.ConfigureAwait(false);
                    workQueue.Enqueue((evt, new Uri(headers.OperationLocation).Segments.Last()));
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

            string reEnqueId = null;
            do
            {
                await workSemaphore.WaitAsync(Config.Cts.Token).ConfigureAwait(false);
                if (Config.Cts.IsCancellationRequested)
                    return;

                MaxQueueLength = Math.Max(MaxQueueLength, workQueue.Count);
                if (!workQueue.TryDequeue(out var item))
                    continue;

                if (item.readOperationId == reEnqueId)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    reEnqueId = null;
                    if (Config.Cts.IsCancellationRequested)
                        return;
                }

                try
                {
                    var result = await cvClient.GetReadOperationResultAsync(item.readOperationId, Config.Cts.Token).ConfigureAwait(false);
                    if (result.Status == TextOperationStatusCodes.Succeeded)
                    {
                        if (result.RecognitionResults.SelectMany(r => r.Lines).Any())
                        {
                            var cnt = true;
                            var prefix = $"[{item.evt.Message.Id % 100:00}]";
                            var ocrText = new StringBuilder($"OCR result of message <{item.evt.Message.JumpLink}>:").AppendLine();
                            Config.Log.Debug($"{prefix} OCR result of message {item.evt.Message.JumpLink}:");
                            var duplicates = new HashSet<string>();
                            foreach (var r in result.RecognitionResults)
                            foreach (var l in r.Lines)
                            {
                                ocrText.AppendLine(l.Text.Sanitize());
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
                            if (!cnt)
                                try
                                {
                                    var botSpamCh = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                                    await botSpamCh.SendAutosplitMessageAsync(ocrText, blockStart: "", blockEnd: "").ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    Config.Log.Warn(ex);
                                }
                        }
                    }
                    else if (result.Status == TextOperationStatusCodes.NotStarted || result.Status == TextOperationStatusCodes.Running)
                    {
                        workQueue.Enqueue(item);
                        reEnqueId ??= item.readOperationId;
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
