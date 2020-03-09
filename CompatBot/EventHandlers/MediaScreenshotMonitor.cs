using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus.EventArgs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.EventHandlers
{
    internal sealed class MediaScreenshotMonitor
    {
        private static readonly ComputerVisionClient client = new ComputerVisionClient(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
        private static readonly SemaphoreSlim workSemaphore = new SemaphoreSlim(0);
        private static readonly ConcurrentQueue<(MessageCreateEventArgs evt, string readOperationId)> workQueue = new ConcurrentQueue<(MessageCreateEventArgs args, string readOperationId)>();
        public static int MaxQueueLength = 0;

        public static async Task OnMessageCreated(MessageCreateEventArgs evt)
        {
            var message = evt.Message;
            if (message == null)
                return;

            if (!(Config.Moderation.Channels.Contains(evt.Channel.Id) || evt.Channel.Name.Contains("help")))
                return;

#if !DEBUG
            if (message.Author.IsBotSafeCheck())
                return;

            if (message.Author.IsSmartlisted(evt.Client))
                return;
#endif

            if (!message.Attachments.Any())
                return;

            var images = Vision.GetImageAttachment(message).ToList();
            var tasks = new List<Task<BatchReadFileHeaders>>(images.Count);
            foreach (var img in images)
                tasks.Add(client.BatchReadFileAsync(img.Url, Config.Cts.Token));
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

        public static async Task ProcessWorkQueue()
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
                    var result = await client.GetReadOperationResultAsync(item.readOperationId, Config.Cts.Token).ConfigureAwait(false);
                    if (result.Status == TextOperationStatusCodes.Succeeded)
                    {
                        var cnt = true;
                        var prefix = $"[{item.evt.Message.Id % 100:00}]";
                        Config.Log.Debug($"{prefix} OCR result of message {item.evt.Message.JumpLink}:");
                        foreach (var r in result.RecognitionResults)
                        foreach (var l in r.Lines)
                        {
                            Config.Log.Debug($"{prefix} {l.Text}");
                            if (cnt && await ContentFilter.FindTriggerAsync(FilterContext.Log, l.Text).ConfigureAwait(false) is Piracystring hit)
                            {
                                FilterAction suppressFlags = 0;
                                if ("media".Equals(item.evt.Channel.Name))
                                    suppressFlags = FilterAction.SendMessage | FilterAction.ShowExplain;
                                await ContentFilter.PerformFilterActions(
                                    item.evt.Client,
                                    item.evt.Message,
                                    hit,
                                    suppressFlags,
                                    l.Text,
                                    "🖼 Screenshot of a pirated game",
                                    "Screenshot of a pirated game"
                                ).ConfigureAwait(false);
                                cnt = false;
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
