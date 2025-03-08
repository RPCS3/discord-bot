using System.Collections.Concurrent;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Utils.Extensions;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;

namespace CompatBot.EventHandlers;

internal sealed class MediaScreenshotMonitor
{
    private readonly ComputerVisionClient cvClient = new(new ApiKeyServiceClientCredentials(Config.AzureComputerVisionKey)) {Endpoint = Config.AzureComputerVisionEndpoint};
    private readonly SemaphoreSlim workSemaphore = new(0);
    private readonly ConcurrentQueue<(MessageCreatedEventArgs evt, Guid readOperationId)> workQueue = new ConcurrentQueue<(MessageCreatedEventArgs args, Guid readOperationId)>();
    public DiscordClient Client { get; internal set; } = null!;
    public static int MaxQueueLength { get; private set; }

    public async Task OnMessageCreated(DiscordClient client, MessageCreatedEventArgs evt)
    {
        if (string.IsNullOrEmpty(Config.AzureComputerVisionKey))
            return;
        
        var message = evt.Message;
        if (message == null)
            return;

        if (!Config.Moderation.OcrChannels.Contains(evt.Channel.Id))
            return;

#if !DEBUG
        if (message.Author.IsBotSafeCheck())
            return;

        if (await message.Author.IsSmartlistedAsync(client).ConfigureAwait(false))
            return;
#endif

        if (!message.Attachments.Any())
            return;

        var images = Vision.GetImageAttachments(message).Select(att => att.Url)
            .Concat(Vision.GetImagesFromEmbeds(message))
            .ToList();
        var tasks = new List<Task<ReadHeaders>>(images.Count);
        foreach (var url in images)
            tasks.Add(cvClient.ReadAsync(url, cancellationToken: Config.Cts.Token));
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
                                    Client,
                                    item.evt.Message,
                                    hit,
                                    suppressFlags,
                                    l.Text,
                                    "🖼 Screenshot of an undesirable content",
                                    "Screenshot of an undesirable content"
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
                                var botSpamCh = await Client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                                await botSpamCh.SendAutosplitMessageAsync(ocrTextBuf, blockStart: "", blockEnd: "").ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Config.Log.Warn(ex);
                            }
                    }
                }
                else if (result.Status is OperationStatusCodes.NotStarted or OperationStatusCodes.Running)
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