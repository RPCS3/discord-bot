using System.Collections.Concurrent;
using CompatApiClient.Utils;
using CompatBot.Commands;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.Ocr;
using CompatBot.Utils.Extensions;

namespace CompatBot.EventHandlers;

internal sealed class MediaScreenshotMonitor
{
    private static readonly SemaphoreSlim WorkSemaphore = new(0);
    private static readonly ConcurrentQueue<(DiscordMessage msg, string imgUrl)> WorkQueue = new();
    public DiscordClient Client { get; internal set; } = null!;
    public static int MaxQueueLength { get; private set; }

    public async Task OnMessageCreated(DiscordClient client, MessageCreatedEventArgs evt)
    {
        if (!OcrProvider.IsAvailable)
            return;
        
        if (evt.Message is not {} message)
            return;

//        if (!Config.Moderation.OcrChannels.Contains(evt.Channel.Id))
//            return;

        if (message.Author.IsBotSafeCheck())
            return;

#if !DEBUG
        if (await message.Author.IsSmartlistedAsync(client).ConfigureAwait(false))
            return;
#endif

        EnqueueOcrTask(evt.Message);
    }

    public static void EnqueueOcrTask(DiscordMessage message)
    {
        if (!message.Attachments.Any())
            return;

        var images = Vision.GetImageAttachments(message)
            .Concat(Vision.GetImagesFromEmbeds(message))
            .ToList();
        foreach (var url in images)
        {
            try
            {
                WorkQueue.Enqueue((message, url));
                WorkSemaphore.Release();
            }
            catch (Exception ex)
            {
                Config.Log.Warn(ex, "Failed to create a new text recognition task");
            }
        }
    }

    public async Task ProcessWorkQueue()
    {
        if (!OcrProvider.IsAvailable)
            return;

        do
        {
            await WorkSemaphore.WaitAsync(Config.Cts.Token).ConfigureAwait(false);
            if (Config.Cts.IsCancellationRequested)
                return;

            MaxQueueLength = Math.Max(MaxQueueLength, WorkQueue.Count);
            if (!WorkQueue.TryDequeue(out var item))
                continue;

            try
            {
                if (await OcrProvider.GetTextAsync(item.imgUrl, Config.Cts.Token).ConfigureAwait(false) is ({Length: >0} result, var confidence)
                    && !Config.Cts.Token.IsCancellationRequested)
                {
                    var cnt = true;
                    var prefix = $"[{item.msg.Id % 100:00}]";
                    var ocrTextBuf = new StringBuilder($"OCR result of message <{item.msg.JumpLink}> ({confidence*100:0.00}%):").AppendLine();
                    Config.Log.Debug($"{prefix} OCR result of message {item.msg.JumpLink} ({confidence*100:0.00}%):");
                    var duplicates = new HashSet<string>();
                    ocrTextBuf.AppendLine(result.Sanitize());
                    Config.Log.Debug($"{prefix} {result}");
                    if (cnt
                        && confidence > 0.65
                        && await ContentFilter.FindTriggerAsync(FilterContext.Chat, result).ConfigureAwait(false) is Piracystring hit
                        && duplicates.Add(hit.String))
                    {
                        FilterAction suppressFlags = 0;
                        if ("media".Equals(item.msg.Channel?.Name))
                        {
                            suppressFlags = FilterAction.SendMessage | FilterAction.ShowExplain;
                        }
                        await ContentFilter.PerformFilterActions(
                            Client,
                            item.msg,
                            hit,
                            suppressFlags,
                            result,
                            "🖼 Screenshot of an undesirable content",
                            "Screenshot of an undesirable content"
                        ).ConfigureAwait(false);
                        cnt &= !hit.Actions.HasFlag(FilterAction.RemoveContent) && !hit.Actions.HasFlag(FilterAction.IssueWarning);
                    }
                    var ocrText = ocrTextBuf.ToString();
                    var hasVkDiagInfo = ocrText.Contains("Vulkan Diagnostics Tool v")
                                        || ocrText.Contains("VkDiag Version:");
                    if (!cnt || hasVkDiagInfo)
                    {
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
            }
            catch (Exception e)
            {
                Config.Log.Warn(e);
            }
        } while (!Config.Cts.IsCancellationRequested);
    }
}